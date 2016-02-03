﻿namespace MsBuild.NuGet.Pack
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using Microsoft.Build.Framework;

    /// <summary>
    ///     The <see cref="BuildNuSpecTask" />
    ///     class executes a NuGet pack on a NuSpec file.
    /// </summary>
    public class BuildNuSpecTask : NuSpecTask
    {
        /// <inheritdoc />
        public override bool Execute()
        {
            var processInfo = new ProcessStartInfo(NuGetPath)
            {
                Arguments = "pack \"" + NuSpecPath + "\" -NoPackageAnalysis -NonInteractive", 
                //CreateNoWindow = false,
                CreateNoWindow = true,
                RedirectStandardError = true, 
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                //WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = OutDir
            };

            //Log.LogMessageFromText("Building NuGet package using " + "'" + processInfo.FileName + " " + processInfo.Arguments + "'", MessageImportance.Normal);
            LogMessage("Building NuGet package using " + "'" + processInfo.FileName + " " + processInfo.Arguments + "'");

            var process = Process.Start(processInfo);

            var completed = process.WaitForExit(30000);
            if (completed)
                process.WaitForExit(); // allow any async calls to complete before releasing

            using (var reader = process.StandardOutput)
            {
                var output = reader.ReadToEnd();

                LogMessage(output);
            }

            if (completed == false)
            {
                LogError("Timeout, creating the NuGet package took longer than 30 seconds.");
            }
            
            using (var errorReader = process.StandardError)
            {
                var error = errorReader.ReadToEnd();

                if (string.IsNullOrWhiteSpace(error) == false)
                {
                    LogError(error);

                    return false;
                }
            }
            process.Close();

            return true;
        }

        /// <summary>
        /// The log message.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        private void LogError(string message)
        {
            if (BuildEngine == null)
            {
                return;
            }

            var nuspecName = Path.GetFileName(NuSpecPath);
            var path = Path.Combine(ProjectDirectory, nuspecName);

            BuildEngine.LogErrorEvent(
                new BuildErrorEventArgs(
                    "BuildNuSpecTask", 
                    "Failure",
                    path, 
                    0, 
                    0, 
                    0, 
                    0, 
                    message, 
                    "BuildNuSpecTask", 
                    "BuildNuSpecTask"));
        }

        /// <summary>
        /// Logs the message.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        /// <param name="importance">
        /// The importance.
        /// </param>
        //private void LogMessage(string message, MessageImportance importance = MessageImportance.Normal)
        //{
        //    BuildEngine.LogMessageEvent(
        //        new BuildMessageEventArgs(
        //            "BuildNuSpecTask: " + message, 
        //            "BuildNuSpecTask", 
        //            "BuildNuSpecTask", 
        //            importance));
        //}

        /// <inheritdoc />
        public IBuildEngine BuildEngine
        {
            get;
            set;
        }

        /// <inheritdoc />
        public ITaskHost HostObject
        {
            get;
            set;
        }

        /// <summary>
        ///     Gets or sets the nuget path.
        /// </summary>
        /// <value>
        ///     The nuget path.
        /// </value>
        [Required]
        public string NuGetPath
        {
            get;
            set;
        }

        /// <summary>
        ///     Gets or sets the nuspec path.
        /// </summary>
        /// <value>
        ///     The nuspec path.
        /// </value>
        [Required]
        public string NuSpecPath
        {
            get;
            set;
        }

        /// <summary>
        ///     The directory in which the built files were written to.
        /// </summary>
        [Required]
        public string OutDir
        {
            get;
            set;
        }

        /// <summary>
        ///     The projects root directory; set to <code>$(MSBuildProjectDirectory)</code> by default.
        /// </summary>
        [Required]
        public string ProjectDirectory
        {
            get;
            set;
        }
    }
}