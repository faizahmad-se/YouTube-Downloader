﻿using System;
using System.IO;
using System.Windows.Forms;
using YouTube_Downloader_DLL.Classes;
using YouTube_Downloader_DLL.FFmpeg;

namespace YouTube_Downloader
{
    public static class Program
    {
        public const string Name = "YouTube Downloader";

        public static bool FFmpegAvailable = true;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Application.SetCompatibleTextRenderingDefault(false);

            Program.FFmpegAvailable = File.Exists(FFmpegProcess.FFmpegPath);

            // Up the connection limit for getting the file sizes of video formats
            System.Net.ServicePointManager.DefaultConnectionLimit = 20;

            LogCleanup.RunAsync();

            new App().Run(args);
        }

        /// <summary>
        /// Automatically saves unhandled Exceptions.
        /// </summary>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Common.SaveException((Exception)e.ExceptionObject);
        }
    }
}
