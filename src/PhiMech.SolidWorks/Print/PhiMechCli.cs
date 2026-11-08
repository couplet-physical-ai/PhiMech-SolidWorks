// Licensed under Apache-2.0; copyright holder in NOTICE.
// Optional command-line adapter for a separately installed, user-configured slicer CLI.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PhiMech.SolidWorks.Print
{
    public static class PhiMechCli
    {
        public static string Command() => Environment.GetEnvironmentVariable("PHIMECH_CMD") ?? "";
        public static bool Installed => !string.IsNullOrWhiteSpace(Command()) && File.Exists(Command());

        public static (int exit, string stdout, string stderr) RunFull(string arguments, string workDir, int timeoutMs,
            IDictionary<string, string> environment = null)
        {
            if (!Installed) return (1, "", "Configure PHIMECH_CMD to point to a local slicer CLI.");
            var psi = new ProcessStartInfo("cmd.exe", "/S /C \"\"" + Command() + "\" " + arguments + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = workDir,
            };
            if (environment != null)
                foreach (var kv in environment) psi.EnvironmentVariables[kv.Key] = kv.Value;
            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch (InvalidOperationException) { }
                    return (-1, "", "timed out after " + timeoutMs / 1000 + " s");
                }
                return (p.ExitCode, (stdout.Result ?? "").Trim(), (stderr.Result ?? "").Trim());
            }
        }
    }
}
