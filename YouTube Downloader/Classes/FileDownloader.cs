﻿// Heavly inspired by:
// http://www.codeproject.com/Articles/35954/C-NET-Background-File-Downloader
//
// Made to fit my own needs and code style.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using IO = System.IO;

namespace YouTube_Downloader.Classes
{
    public class FileDownloader : IDisposable
    {
        BackgroundWorker _downloader;

        public event EventHandler CalculatedTotalFileSize;
        public event EventHandler Canceled;
        public event EventHandler Completed;
        public event EventHandler FileDownloadComplete;
        public event ExceptionEventHandler FileDownloadFailed;
        public event EventHandler FileDownloadSucceeded;
        public event EventHandler Paused;
        public event EventHandler ProgressChanged;
        public event EventHandler Resumed;
        public event EventHandler Started;
        public event EventHandler Stopped;

        public int PackageSize { get; set; }
        public int Speed { get; set; }
        public int StopWatchCyclesAmount { get; set; }

        public bool CanPause
        {
            get { return this.IsBusy && !this.IsPaused && !_downloader.CancellationPending; }
        }
        public bool CanResume
        {
            get { return this.IsBusy && this.IsPaused && !_downloader.CancellationPending; }
        }
        public bool CanStart
        {
            get { return !this.IsBusy; }
        }
        public bool CanStop
        {
            get { return this.IsBusy && !_downloader.CancellationPending; }
        }
        public bool DeleteUnfinishedFilesOnCancel { get; set; }
        public bool IsBusy { get; private set; }
        public bool IsPaused { get; private set; }
        public bool WasCanceled { get; set; }

        public long TotalProgress { get; set; }
        public long TotalSize { get; private set; }

        public string Directory { get; set; }

        public DownloadFile CurrentFile { get; private set; }

        public List<DownloadFile> Files { get; set; }

        public delegate void ExceptionEventHandler(object sender, Exception ex);

        private enum BackgroundEvents
        {
            FileDownloadSucceeded,
            ProgressChanged
        }

        #region IDisposable Members

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Free other state (managed objects)
                    _downloader.Dispose();
                }
                // Free your own state (unmanaged objects)
                // Set large fields to null
                this.Files = null;
            }
        }

        #endregion

        public FileDownloader()
        {
            _downloader = new BackgroundWorker()
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            _downloader.DoWork += downloader_DoWork;
            _downloader.ProgressChanged += downloader_ProgressChanged;
            _downloader.RunWorkerCompleted += downloader_RunWorkerCompleted;

            this.PackageSize = 4096;
            this.StopWatchCyclesAmount = 5;
            this.DeleteUnfinishedFilesOnCancel = true;
            this.Files = new List<DownloadFile>();
        }

        public void Pause()
        {
            if (!this.IsBusy || this.IsPaused)
                return;

            this.IsPaused = true;
            this.OnPaused();
        }

        public void Resume()
        {
            if (!this.IsBusy || !this.IsPaused)
                return;

            this.IsPaused = false;
            this.OnResumed();
        }

        public void Start()
        {
            this.IsBusy = true;
            this.WasCanceled = false;
            this.TotalProgress = 0;

            _downloader.RunWorkerAsync();

            this.OnStarted();
        }

        public void Stop(bool deleteUnfinishedFiles)
        {
            this.IsBusy = false;
            this.IsPaused = false;
            this.WasCanceled = true;

            _downloader.CancelAsync();

            this.OnCanceled();
        }

        public Double TotalPercentage()
        {
            return Math.Round((Double)this.TotalProgress / this.TotalSize * 100, 2);
        }

        private void CalculateTotalFileSize()
        {
            this.TotalSize = 0;

            foreach (var file in this.Files)
            {
                try
                {
                    WebRequest webReq = (WebRequest)WebRequest.Create(file.Url);
                    WebResponse webResp = (WebResponse)webReq.GetResponse();

                    this.TotalSize += webResp.ContentLength;

                    webResp.Close();
                }
                catch (Exception) { }
            }

            this.OnCalculatedTotalFileSize();
        }

        private void CleanupFiles()
        {
            new Thread(delegate()
            {
                var dict = new Dictionary<string, int>();
                var keys = new List<string>();

                foreach (var file in this.Files)
                {
                    if (!file.IsFinished)
                    {
                        dict.Add(file.Path, 0);
                        keys.Add(file.Path);
                    }
                }

                while (dict.Count > 0)
                {
                    foreach (string key in keys)
                    {
                        try
                        {
                            if (File.Exists(key))
                                File.Delete(key);

                            // Remove file from dictionary since it either got deleted
                            // or it doesn't exist anymore.
                            dict.Remove(key);
                        }
                        catch
                        {
                            if (dict[key] == 10)
                            {
                                dict.Remove(key);
                            }
                            else
                            {
                                dict[key]++;
                            }
                        }
                    }

                    Thread.Sleep(2000);
                }
            }).Start();
        }

        private void DownloadFile()
        {
            this.OnFileDownloadComplete();

            long size = 0;

            byte[] readBytes = new byte[this.PackageSize];
            int currentPackageSize;
            Stopwatch speedTimer = new Stopwatch();
            int readings = 0;
            Exception exception = null;

            FileStream writer = new FileStream(Path.Combine(this.Directory, this.CurrentFile.Name), FileMode.Create);

            WebRequest webReq;
            WebResponse webResp = null;

            try
            {
                webReq = (WebRequest)WebRequest.Create(this.CurrentFile.Url);
                webResp = (WebResponse)webReq.GetResponse();

                size = webResp.ContentLength;
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            this.CurrentFile.TotalFileSize = size;

            if (exception != null)
            {
                _downloader.ReportProgress(0, exception);
            }
            else
            {
                this.CurrentFile.Progress = 0;

                while (this.CurrentFile.Progress < size && !_downloader.CancellationPending)
                {
                    while (this.IsPaused)
                        System.Threading.Thread.Sleep(100);

                    speedTimer.Start();

                    currentPackageSize = webResp.GetResponseStream().Read(readBytes, 0, this.PackageSize);

                    this.CurrentFile.Progress += currentPackageSize;
                    this.TotalProgress += currentPackageSize;
                    
                    // Raise ProgressChanged event
                    _downloader.ReportProgress(-1, BackgroundEvents.ProgressChanged);

                    writer.Write(readBytes, 0, currentPackageSize);
                    readings += 1;

                    if (readings >= this.StopWatchCyclesAmount)
                    {
                        this.Speed = (int)(this.PackageSize * StopWatchCyclesAmount * 1000 / (speedTimer.ElapsedMilliseconds + 1));
                        speedTimer.Reset();
                        readings = 0;
                    }
                }

                speedTimer.Stop();
                writer.Close();
                webResp.Close();

                if (!_downloader.CancellationPending)
                {
                    this.CurrentFile.IsFinished = true;
                    
                    _downloader.ReportProgress(-1, BackgroundEvents.FileDownloadSucceeded);
                }
            }
        }

        private void downloader_DoWork(object sender, DoWorkEventArgs e)
        {
            this.CalculateTotalFileSize();

            if (!IO.Directory.Exists(this.Directory))
                IO.Directory.CreateDirectory(this.Directory);

            foreach (var file in this.Files)
            {
                this.CurrentFile = file;
                DownloadFile();

                if (_downloader.CancellationPending)
                {
                    CleanupFiles();
                }
            }
        }

        private void downloader_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.UserState is Exception)
            {
                this.OnFileDownloadFailed(e.UserState as Exception);
            }
            else if (e.UserState is BackgroundEvents)
            {
                switch((BackgroundEvents)e.UserState)
                {
                    case BackgroundEvents.FileDownloadSucceeded:
                        break;
                    case BackgroundEvents.ProgressChanged:
                        break;
                }
            }
        }

        private void downloader_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            this.IsBusy = this.IsPaused = false;

            if (!this.WasCanceled)
                this.OnCompleted();
            else
                this.OnCanceled();

            this.OnStopped();
        }

        protected void OnCalculatedTotalFileSize()
        {
            if (this.CalculatedTotalFileSize != null)
                this.CalculatedTotalFileSize(this, EventArgs.Empty);
        }

        protected void OnCanceled()
        {
            if (this.Canceled != null)
                this.Canceled(this, EventArgs.Empty);
        }

        protected void OnCompleted()
        {
            if (this.Completed != null)
                this.Completed(this, EventArgs.Empty);
        }

        protected void OnFileDownloadComplete()
        {
            if (FileDownloadComplete != null)
                FileDownloadComplete(this, EventArgs.Empty);
        }

        protected void OnFileDownloadFailed(Exception exception)
        {
            if (this.FileDownloadFailed != null)
                this.FileDownloadFailed(this, exception);
        }

        protected void OnFileDownloadSucceeded()
        {
            if (FileDownloadSucceeded != null)
                FileDownloadSucceeded(this, EventArgs.Empty);
        }

        protected void OnPaused()
        {
            if (Paused != null)
                Paused(this, EventArgs.Empty);
        }

        protected void OnProgressChanged()
        {
            if (ProgressChanged != null)
                ProgressChanged(this, EventArgs.Empty);
        }

        protected void OnResumed()
        {
            if (Resumed != null)
                Resumed(this, EventArgs.Empty);
        }

        protected void OnStarted()
        {
            if (this.Started != null)
                this.Started(this, EventArgs.Empty);
        }

        protected void OnStopped()
        {
            if (this.Stopped != null)
                this.Stopped(this, EventArgs.Empty);
        }
    }

    public class DownloadFile
    {
        public bool IsFinished { get; set; }

        public long Progress { get; set; }
        public long TotalFileSize { get; set; }

        public string Name
        {
            get { return IO.Path.GetFileName(this.Path); }
        }
        public string Path { get; set; }
        public string Url { get; set; }

        public DownloadFile(string path, string url)
        {
            this.Path = path;
            this.Url = url;
        }
    }
}