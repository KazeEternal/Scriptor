using Scripts.Scriptor;
using Scripts.Scriptor.Attributor;
using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.Diagnostics;
using System.IO;

namespace Scripts.Scripting
{
    [ScriptCollectionName("Pond Web Deployment")]
    [ScriptCollectionDescription("Builds and deploys Pond Web WAR artifacts using configurable paths and options.")]
    [ScriptPackageDependency("SSH.NET", "2020.0.2")]
    [ScriptPackageDependency("SshNet.Security.Cryptography", "1.3.0")]
    public class PondWebDeploymentScripts : IScriptCollection
    {
        [ScriptRoutine("Deploy Pond Web", "Build Pond Web with Maven (optional) and copy WAR to deployment target.")]
        public static void DeployPondWeb(
            IScriptContext context,
            [Parameter("Build with Maven", "Set true to run Maven package before copy.", "Boolean", true)]
            bool buildWithMaven,
            [Parameter("Maven Working Directory", "Folder containing pom.xml for PondWeb build.", "Path", "D:\\Development\\PondWeb\\pondweb")]
            string mavenWorkingDirectory,
            [Parameter("Maven Arguments", "Arguments passed to mvn when build is enabled.", "Examples: package | package -DskipTests", "package")]
            string mavenArguments,
            [Parameter("Source WAR Path", "Path to built WAR file to deploy.", "Path", "D:\\Development\\PondWeb\\pondweb\\target\\pondweb.war")]
            string sourceWarPath,
            [Parameter("Destination WAR Path", "Deployment WAR path (e.g., ROOT.war on target share).", "Path", "W:\\ROOT.war")]
            string destinationWarPath,
            [Parameter("Overwrite Destination", "Set true to overwrite destination file if present.", "Boolean", true)]
            bool overwriteDestination,
            [Parameter("Preserve Source Timestamp", "Set true to copy source timestamp to destination file.", "Boolean", true)]
            bool preserveSourceTimestamp,
            [Parameter("Restart Remote Tomcat Over SSH", "Set true to issue restart command on remote host after copy.", "Boolean", false)]
            bool restartRemoteTomcatOverSsh,
            [Parameter("SSH Host", "Remote Linux host for restart command.", "Hostname or IP", "")]
            string sshHost,
            [Parameter("SSH Port", "SSH port for remote host.", "Integer", 22)]
            int sshPort,
            [Parameter("SSH Username", "SSH username for remote host.", "String", "")]
            string sshUsername,
            [Parameter("SSH Password", "SSH password (optional when using key auth).", "String", "")]
            string sshPassword,
            [Parameter("SSH Private Key Path", "Path to private key file for key-based auth (optional).", "Path", "")]
            string sshPrivateKeyPath,
            [Parameter("SSH Private Key Passphrase", "Passphrase for encrypted private key (optional).", "String", "")]
            string sshPrivateKeyPassphrase,
            [Parameter("Remote Restart Command", "Command to restart service on remote host.", "String", "sudo systemctl restart tomcat")]
            string remoteRestartCommand)
        {
            var deploymentProgress = context.CreateProgressChannel("Pond Web Deployment");

            try
            {
                deploymentProgress.Report(5, "Validating input paths");

                if (string.IsNullOrWhiteSpace(sourceWarPath))
                {
                    throw new ArgumentException("Source WAR Path is required.");
                }

                if (string.IsNullOrWhiteSpace(destinationWarPath))
                {
                    throw new ArgumentException("Destination WAR Path is required.");
                }

                if (buildWithMaven)
                {
                    deploymentProgress.Report(15, "Running Maven build");

                    if (string.IsNullOrWhiteSpace(mavenWorkingDirectory) || !Directory.Exists(mavenWorkingDirectory))
                    {
                        throw new DirectoryNotFoundException($"Maven Working Directory not found: {mavenWorkingDirectory}");
                    }

                    RunMavenBuild(mavenWorkingDirectory, mavenArguments);
                }

                deploymentProgress.Report(70, "Preparing deployment copy");
                var copiedInfo = CopyWarArtifact(sourceWarPath, destinationWarPath, overwriteDestination, preserveSourceTimestamp);

                if (restartRemoteTomcatOverSsh)
                {
                    deploymentProgress.Report(85, "Restarting remote Tomcat over SSH");
                    ExecuteRemoteCommandWithFallback(
                        sshHost,
                        sshPort,
                        sshUsername,
                        sshPassword,
                        sshPrivateKeyPath,
                        sshPrivateKeyPassphrase,
                        remoteRestartCommand);
                }

                deploymentProgress.Report(100, "Deploy complete");

                Logger.WriteLine(Logger.LogLevel.Event, "Pond Web deploy complete.");
                Logger.WriteLine(Logger.LogLevel.Event, "  Source      : {0}", sourceWarPath);
                Logger.WriteLine(Logger.LogLevel.Event, "  Destination : {0}", copiedInfo.FullName);
                Logger.WriteLine(Logger.LogLevel.Event, "  Size (bytes): {0}", copiedInfo.Length);
                Logger.WriteLine(Logger.LogLevel.Event, "  LastWriteUtc: {0:O}", copiedInfo.LastWriteTimeUtc);
                if (restartRemoteTomcatOverSsh)
                {
                    Logger.WriteLine(Logger.LogLevel.Event, "  Remote restart command executed on {0}:{1}", sshHost, sshPort);
                }

                context.IsSuccess = true;
            }
            catch (Exception ex)
            {
                deploymentProgress.LogError("Deployment failed: {0}", ex.Message);
                Logger.WriteLine(Logger.LogLevel.Error, "Pond Web deploy failed: {0}", ex);
                context.IsSuccess = false;
                throw;
            }
        }

        [ScriptRoutine("Deploy Pond Web (Copy Only)", "Copies an existing Pond Web WAR to the deployment target without running Maven.")]
        public static void CopyPondWebWarOnly(
            IScriptContext context,
            [Parameter("Source WAR Path", "Path to built WAR file to deploy.", "Path", "D:\\Development\\PondWeb\\pondweb\\target\\pondweb.war")]
            string sourceWarPath,
            [Parameter("Destination WAR Path", "Deployment WAR path (e.g., ROOT.war on target share).", "Path", "W:\\ROOT.war")]
            string destinationWarPath,
            [Parameter("Overwrite Destination", "Set true to overwrite destination file if present.", "Boolean", true)]
            bool overwriteDestination,
            [Parameter("Preserve Source Timestamp", "Set true to copy source timestamp to destination file.", "Boolean", true)]
            bool preserveSourceTimestamp,
            [Parameter("Restart Remote Tomcat Over SSH", "Set true to issue restart command on remote host after copy.", "Boolean", false)]
            bool restartRemoteTomcatOverSsh,
            [Parameter("SSH Host", "Remote Linux host for restart command.", "Hostname or IP", "")]
            string sshHost,
            [Parameter("SSH Port", "SSH port for remote host.", "Integer", 22)]
            int sshPort,
            [Parameter("SSH Username", "SSH username for remote host.", "String", "")]
            string sshUsername,
            [Parameter("SSH Password", "SSH password (optional when using key auth).", "String", "")]
            string sshPassword,
            [Parameter("SSH Private Key Path", "Path to private key file for key-based auth (optional).", "Path", "")]
            string sshPrivateKeyPath,
            [Parameter("SSH Private Key Passphrase", "Passphrase for encrypted private key (optional).", "String", "")]
            string sshPrivateKeyPassphrase,
            [Parameter("Remote Restart Command", "Command to restart service on remote host.", "String", "sudo systemctl restart tomcat")]
            string remoteRestartCommand)
        {
            var copyProgress = context.CreateProgressChannel("Pond Web Copy Only");

            try
            {
                copyProgress.Report(10, "Validating paths");
                var copiedInfo = CopyWarArtifact(sourceWarPath, destinationWarPath, overwriteDestination, preserveSourceTimestamp);

                if (restartRemoteTomcatOverSsh)
                {
                    copyProgress.Report(85, "Restarting remote Tomcat over SSH");
                    ExecuteRemoteCommandWithFallback(
                        sshHost,
                        sshPort,
                        sshUsername,
                        sshPassword,
                        sshPrivateKeyPath,
                        sshPrivateKeyPassphrase,
                        remoteRestartCommand);
                }

                copyProgress.Report(100, "Copy complete");

                Logger.WriteLine(Logger.LogLevel.Event, "Pond Web copy-only deploy complete.");
                Logger.WriteLine(Logger.LogLevel.Event, "  Source      : {0}", sourceWarPath);
                Logger.WriteLine(Logger.LogLevel.Event, "  Destination : {0}", copiedInfo.FullName);
                Logger.WriteLine(Logger.LogLevel.Event, "  Size (bytes): {0}", copiedInfo.Length);
                Logger.WriteLine(Logger.LogLevel.Event, "  LastWriteUtc: {0:O}", copiedInfo.LastWriteTimeUtc);
                if (restartRemoteTomcatOverSsh)
                {
                    Logger.WriteLine(Logger.LogLevel.Event, "  Remote restart command executed on {0}:{1}", sshHost, sshPort);
                }

                context.IsSuccess = true;
            }
            catch (Exception ex)
            {
                copyProgress.LogError("Copy-only deployment failed: {0}", ex.Message);
                Logger.WriteLine(Logger.LogLevel.Error, "Pond Web copy-only deploy failed: {0}", ex);
                context.IsSuccess = false;
                throw;
            }
        }

        private static void ExecuteRemoteCommandWithFallback(
            string host,
            int port,
            string username,
            string password,
            string privateKeyPath,
            string privateKeyPassphrase,
            string command)
        {
            try
            {
                ExecuteRemoteCommandOverSsh(
                    host,
                    port,
                    username,
                    password,
                    privateKeyPath,
                    privateKeyPassphrase,
                    command);
            }
            catch (FileNotFoundException missingAssemblyEx)
            {
                Logger.WriteLine(Logger.LogLevel.Warning,
                    "SSH.NET dependency load failed ({0}). Falling back to system ssh command.",
                    missingAssemblyEx.FileName ?? missingAssemblyEx.Message);

                ExecuteRemoteCommandViaSystemSsh(host, port, username, privateKeyPath, command);
            }
        }

        private static FileInfo CopyWarArtifact(
            string sourceWarPath,
            string destinationWarPath,
            bool overwriteDestination,
            bool preserveSourceTimestamp)
        {
            var sourceInfo = new FileInfo(sourceWarPath);
            if (!sourceInfo.Exists)
            {
                throw new FileNotFoundException("Source WAR file not found.", sourceWarPath);
            }

            var destinationInfo = new FileInfo(destinationWarPath);
            var destinationDirectory = destinationInfo.DirectoryName;
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new InvalidOperationException($"Unable to resolve destination directory for {destinationWarPath}");
            }

            if (!Directory.Exists(destinationDirectory))
            {
                throw new DirectoryNotFoundException($"Destination directory does not exist: {destinationDirectory}");
            }

            if (destinationInfo.Exists && !overwriteDestination)
            {
                throw new IOException($"Destination exists and Overwrite Destination is false: {destinationWarPath}");
            }

            File.Copy(sourceWarPath, destinationWarPath, overwriteDestination);

            if (preserveSourceTimestamp)
            {
                File.SetLastWriteTimeUtc(destinationWarPath, sourceInfo.LastWriteTimeUtc);
            }

            return new FileInfo(destinationWarPath);
        }

        private static void RunMavenBuild(string workingDirectory, string arguments)
        {
            var args = string.IsNullOrWhiteSpace(arguments) ? "package" : arguments.Trim();

            ProcessStartInfo CreateStartInfo(string fileName) => new()
            {
                FileName = fileName,
                Arguments = args,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var processStartInfo = CreateStartInfo("mvn");
            Process? process = null;
            try
            {
                process = new Process { StartInfo = processStartInfo };
                if (!process.Start())
                {
                    throw new InvalidOperationException("Failed to start Maven process.");
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                process?.Dispose();
                processStartInfo = CreateStartInfo("mvn.cmd");
                process = new Process { StartInfo = processStartInfo };
                if (!process.Start())
                {
                    throw new InvalidOperationException("Failed to start Maven process. Ensure Maven is installed and on PATH.");
                }
            }
            using (process)
            {
                process.OutputDataReceived += (_, eventArgs) =>
                {
                    if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                    {
                        Logger.WriteLine(Logger.LogLevel.Event, "[mvn] {0}", eventArgs.Data);
                    }
                };

                process.ErrorDataReceived += (_, eventArgs) =>
                {
                    if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                    {
                        Logger.WriteLine(Logger.LogLevel.Warning, "[mvn] {0}", eventArgs.Data);
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Maven build failed with exit code {process.ExitCode}.");
                }
            }
        }

        private static void ExecuteRemoteCommandOverSsh(
            string host,
            int port,
            string username,
            string password,
            string privateKeyPath,
            string privateKeyPassphrase,
            string command)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("SSH Host is required when remote restart is enabled.", nameof(host));
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("SSH Username is required when remote restart is enabled.", nameof(username));
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("Remote Restart Command is required when remote restart is enabled.", nameof(command));
            }

            if (port <= 0)
            {
                port = 22;
            }

            Logger.WriteLine(Logger.LogLevel.Event, "Executing remote SSH command on {0}:{1}", host, port);

            ConnectionInfo connectionInfo;
            if (!string.IsNullOrWhiteSpace(privateKeyPath))
            {
                if (!File.Exists(privateKeyPath))
                {
                    throw new FileNotFoundException("SSH private key file not found.", privateKeyPath);
                }

                var keyFile = string.IsNullOrWhiteSpace(privateKeyPassphrase)
                    ? new PrivateKeyFile(privateKeyPath)
                    : new PrivateKeyFile(privateKeyPath, privateKeyPassphrase);

                connectionInfo = new ConnectionInfo(
                    host,
                    port,
                    username,
                    new PrivateKeyAuthenticationMethod(username, keyFile));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(password))
                {
                    throw new ArgumentException("SSH Password is required when private key is not provided.", nameof(password));
                }

                connectionInfo = new PasswordConnectionInfo(host, port, username, password);
            }

            using var client = new SshClient(connectionInfo);
            client.Connect();
            if (!client.IsConnected)
            {
                throw new SshConnectionException("Unable to connect to remote host via SSH.");
            }

            var cmd = client.RunCommand(command);
            if (!string.IsNullOrWhiteSpace(cmd.Result))
            {
                Logger.WriteLine(Logger.LogLevel.Event, "[ssh] {0}", cmd.Result.Trim());
            }

            if (!string.IsNullOrWhiteSpace(cmd.Error))
            {
                Logger.WriteLine(Logger.LogLevel.Warning, "[ssh] {0}", cmd.Error.Trim());
            }

            if (cmd.ExitStatus != 0)
            {
                throw new InvalidOperationException($"Remote command failed with exit code {cmd.ExitStatus}.");
            }

            client.Disconnect();
        }

        private static void ExecuteRemoteCommandViaSystemSsh(
            string host,
            int port,
            string username,
            string privateKeyPath,
            string command)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("SSH Host is required when remote restart is enabled.", nameof(host));
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("SSH Username is required when remote restart is enabled.", nameof(username));
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("Remote Restart Command is required when remote restart is enabled.", nameof(command));
            }

            if (port <= 0)
            {
                port = 22;
            }

            string keyOption = string.Empty;
            if (!string.IsNullOrWhiteSpace(privateKeyPath))
            {
                if (!File.Exists(privateKeyPath))
                {
                    throw new FileNotFoundException("SSH private key file not found.", privateKeyPath);
                }

                keyOption = $" -i \"{privateKeyPath}\"";
            }

            string escapedCommand = command.Replace("\"", "\\\"");
            string args = $"-p {port}{keyOption} {username}@{host} \"{escapedCommand}\"";

            Logger.WriteLine(Logger.LogLevel.Event, "Executing remote command via system ssh on {0}:{1}", host, port);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "ssh",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start system ssh process.");
            }

            string stdOut = process.StandardOutput.ReadToEnd();
            string stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(stdOut))
            {
                Logger.WriteLine(Logger.LogLevel.Event, "[ssh-cli] {0}", stdOut.Trim());
            }

            if (!string.IsNullOrWhiteSpace(stdErr))
            {
                Logger.WriteLine(Logger.LogLevel.Warning, "[ssh-cli] {0}", stdErr.Trim());
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"System ssh command failed with exit code {process.ExitCode}.");
            }
        }
    }
}
