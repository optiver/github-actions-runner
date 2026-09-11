using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
            return GetCredential(ReadCredentials(filePath), host);
        }

        public static NetRcCredential GetCredential(IReadOnlyDictionary<string, NetRcCredential> credentials, string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return null;
            }

            return credentials.TryGetValue(host, out var credential) || credentials.TryGetValue(string.Empty, out credential)
                ? credential
                : null;
        }

        // An empty machine name represents the default entry. Read once per
        // download attempt so all redirect hops use the same credential snapshot.
        public static IReadOnlyDictionary<string, NetRcCredential> ReadCredentials(string filePath)
        {
            var machines = new Dictionary<string, NetRcCredential>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(filePath))
            {
                return machines;
            }

            try
            {
                using var reader = File.OpenText(filePath);
                string currentMachine = null;
                string login = null;
                string password = null;

                void FlushEntry()
                {
                    if (currentMachine != null && password != null)
                    {
                        // First matching entry wins, as with other .netrc consumers.
                        machines.TryAdd(currentMachine, new NetRcCredential(login ?? string.Empty, password));
                    }

                    currentMachine = null;
                    login = null;
                    password = null;
                }

                string token;
                while ((token = ReadToken(reader)) != null)
                {
                    switch (token)
                    {
                        case "machine":
                            FlushEntry();
                            currentMachine = ReadToken(reader);
                            if (currentMachine == string.Empty)
                            {
                                currentMachine = null;
                            }
                            break;
                        case "default":
                            FlushEntry();
                            currentMachine = string.Empty;
                            break;
                        case "login":
                            login = ReadToken(reader);
                            break;
                        case "password":
                            password = ReadToken(reader);
                            break;
                        case "account":
                            // Recognized but unused; consume the value.
                            ReadToken(reader);
                            break;
                        case "macdef":
                            // Skip the macro name and body through the next blank line.
                            reader.ReadLine();
                            string line;
                            while ((line = reader.ReadLine()) != null && !string.IsNullOrWhiteSpace(line))
                            {
                            }
                            break;
                    }
                }

                FlushEntry();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
            {
                // An unreadable or malformed file must not supply partial credentials.
                machines.Clear();
            }

            return machines;
        }

        private static string ReadToken(TextReader reader)
        {
            int next;
            while ((next = reader.Peek()) != -1)
            {
                if (char.IsWhiteSpace((char)next))
                {
                    reader.Read();
                }
                else if (next == '#')
                {
                    reader.ReadLine();
                }
                else
                {
                    break;
                }
            }

            if (next == -1)
            {
                return null;
            }

            bool quoted = next == '"';
            if (quoted)
            {
                reader.Read();
            }
            var token = new StringBuilder();
            while ((next = reader.Peek()) != -1)
            {
                if (!quoted && char.IsWhiteSpace((char)next))
                {
                    return token.ToString();
                }

                reader.Read();
                if (quoted && next == '"')
                {
                    return token.ToString();
                }

                if (quoted && (next == '\r' || next == '\n'))
                {
                    break;
                }

                if (quoted && next == '\\')
                {
                    next = reader.Read();
                    if (next == -1 || next == '\r' || next == '\n')
                    {
                        break;
                    }
                    token.Append(next switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => (char)next
                    });
                }
                else
                {
                    token.Append((char)next);
                }
            }

            if (quoted)
            {
                throw new FormatException("Unterminated quoted value in .netrc.");
            }
            return token.ToString();
        }
    }
}
