using System;
using System.Collections.Generic;
using System.IO;

namespace GitHub.Runner.Sdk
{
    public sealed class NetRcCredential
    {
        public NetRcCredential(string login, string password)
        {
            Login = login;
            Password = password;
        }

        public string Login { get; }

        public string Password { get; }
    }

    /// <summary>
    /// Minimal reader for the standard .netrc credential file, matching the
    /// lookup behavior other HTTP clients on the host (e.g. curl) already
    /// honour. Used to authenticate archive downloads that get redirected to
    /// hosts (such as enterprise caching servers) the runner holds no service
    /// credential for.
    /// </summary>
    public static class NetRcUtil
    {
        /// <summary>
        /// Resolves the credential file the same way curl does: the NETRC
        /// environment variable wins, otherwise ~/.netrc (falling back to
        /// ~/_netrc, the spelling some Windows tools use). Returns null when
        /// no file exists.
        /// </summary>
        public static string ResolveFilePath()
        {
            var netrcEnv = Environment.GetEnvironmentVariable("NETRC");
            if (!string.IsNullOrEmpty(netrcEnv))
            {
                return File.Exists(netrcEnv) ? netrcEnv : null;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                return null;
            }

            foreach (var fileName in new[] { ".netrc", "_netrc" })
            {
                var candidate = Path.Combine(home, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        public static NetRcCredential GetCredential(string host)
        {
            return GetCredential(ResolveFilePath(), host);
        }

        public static NetRcCredential GetCredential(string filePath, string host)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(host) || !File.Exists(filePath))
            {
                return null;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            var machines = new Dictionary<string, NetRcCredential>(StringComparer.OrdinalIgnoreCase);
            NetRcCredential defaultCredential = null;

            string currentMachine = null;
            bool inDefault = false;
            bool skippingMacro = false;
            string login = null;
            string password = null;

            void FlushEntry()
            {
                if (inDefault && defaultCredential == null && password != null)
                {
                    defaultCredential = new NetRcCredential(login ?? string.Empty, password);
                }
                else if (currentMachine != null && password != null && !machines.ContainsKey(currentMachine))
                {
                    // First matching entry wins, as with other .netrc consumers.
                    machines[currentMachine] = new NetRcCredential(login ?? string.Empty, password);
                }

                currentMachine = null;
                inDefault = false;
                login = null;
                password = null;
            }

            foreach (var line in lines)
            {
                if (skippingMacro)
                {
                    // A macdef body runs until the first blank line.
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        skippingMacro = false;
                    }
                    continue;
                }

                var tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < tokens.Length; i++)
                {
                    switch (tokens[i])
                    {
                        case "machine":
                            FlushEntry();
                            if (i + 1 < tokens.Length)
                            {
                                currentMachine = tokens[++i];
                            }
                            break;
                        case "default":
                            FlushEntry();
                            inDefault = true;
                            break;
                        case "login":
                            if (i + 1 < tokens.Length)
                            {
                                login = tokens[++i];
                            }
                            break;
                        case "password":
                            if (i + 1 < tokens.Length)
                            {
                                password = tokens[++i];
                            }
                            break;
                        case "account":
                            // Recognized but unused; consume the value.
                            i++;
                            break;
                        case "macdef":
                            i = tokens.Length; // rest of this line is the macro name
                            skippingMacro = true;
                            break;
                        default:
                            // Unknown token; ignore to stay permissive about hand-edited files.
                            break;
                    }
                }
            }

            FlushEntry();

            if (machines.TryGetValue(host, out var credential))
            {
                return credential;
            }

            return defaultCredential;
        }
    }
}
