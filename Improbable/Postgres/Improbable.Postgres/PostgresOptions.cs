using System;
using System.Collections.Generic;
using System.Linq;
using Improbable.Stdlib;

namespace Improbable.Postgres
{
    public class PostgresOptions : IPostgresOptions
    {
        public const string HostFlagName = "postgres_host";
        public const string UserNameFlagName = "postgres_user";
        public const string PasswordFlagName = "postgres_password";
        public const string DatabaseFlagName = "postgres_database";
        public const string AdditionalFlagName = "postgres_additional";

        private string? cachedConnectionString;
        private readonly object rootLock = new object();

        private readonly Dictionary<string, string> flagValues = new Dictionary<string, string>
        {
            {HostFlagName, "127.0.0.1"},
            {UserNameFlagName, "postgres"},
            {PasswordFlagName, "DO_NOT_USE_IN_PRODUCTION"},
            {DatabaseFlagName, "postgres"},
            {AdditionalFlagName, string.Empty}
        };

        private readonly List<string> keys;

        public delegate string GetStringDelegate(string flagName, string currentFlagValue);

        public PostgresOptions(GetStringDelegate getter)
        {
            keys = flagValues.Keys.ToList();

            foreach (var (key, currentValue) in flagValues.ToList())
            {
                var value = getter(key, currentValue);

                if (!string.IsNullOrEmpty(value))
                {
                    flagValues[key] = value;
                }
            }
        }

        public PostgresOptions(IPostgresOptions options)
        : this((key, value) => GetFromIOptions(options, key, value))
        {
        }

        public static string GetFromIOptions(IPostgresOptions options, string key, string value)
        {
            var optionValue = key switch
            {
                HostFlagName => options.PostgresHost,
                UserNameFlagName => options.PostgresUserName,
                PasswordFlagName => options.PostgresPassword,
                DatabaseFlagName => options.PostgresDatabase,
                AdditionalFlagName => options.PostgresAdditionalOptions,
                _ => throw new InvalidOperationException($"Unknown Postgres flag {key}")
            };

            return string.IsNullOrEmpty(optionValue) ? value : optionValue;
        }

        public string PostgresHost
        {
            get
            {
                lock (rootLock)
                {
                    return flagValues[HostFlagName];
                }
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentNullException(nameof(PostgresHost));
                }

                lock (rootLock)
                {
                    flagValues[HostFlagName] = value;
                    cachedConnectionString = null;
                }
            }
        }

        public string PostgresUserName
        {
            get
            {
                lock (rootLock)
                {
                    return flagValues[UserNameFlagName];
                }
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentNullException(nameof(PostgresUserName));
                }

                lock (rootLock)
                {
                    flagValues[UserNameFlagName] = value;
                    cachedConnectionString = null;
                }
            }
        }

        public string PostgresPassword
        {
            get
            {
                lock (rootLock)
                {
                    return flagValues[PasswordFlagName];
                }
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentNullException(nameof(PostgresPassword));
                }

                lock (rootLock)
                {
                    flagValues[PasswordFlagName] = value;
                    cachedConnectionString = null;
                }
            }
        }

        public string PostgresDatabase
        {
            get
            {
                lock (rootLock)
                {
                    return flagValues[DatabaseFlagName];
                }
            }
            set
            {
                lock (rootLock)
                {
                    flagValues[DatabaseFlagName] = value;
                    cachedConnectionString = null;
                }
            }
        }

        public string PostgresAdditionalOptions
        {
            get
            {
                lock (rootLock)
                {
                    return flagValues[AdditionalFlagName];
                }
            }
            set
            {
                lock (rootLock)
                {
                    flagValues[AdditionalFlagName] = value;
                    cachedConnectionString = null;
                }
            }
        }

        public string ConnectionString
        {
            get
            {
                lock (rootLock)
                {
                    return cachedConnectionString ??= AsPostgresConnectionString();
                }
            }
        }

        public void ProcessOpList(OpList opList)
        {
            foreach (var key in keys)
            {
                var value = string.Empty;
                if (!opList.TryGetWorkerFlagChange(key, ref value))
                {
                    continue;
                }

                lock (rootLock)
                {
                    if (string.IsNullOrEmpty(value) && !IsEmptyStringAllowed(key))
                    {
                        continue;
                    }

                    flagValues[key] = value;
                    cachedConnectionString = null;
                }
            }
        }

        private static bool IsEmptyStringAllowed(string key)
        {
            return key switch
            {
                HostFlagName => false,
                UserNameFlagName => false,
                PasswordFlagName => false,
                _ => true
            };
        }

        private string AsPostgresConnectionString()
        {
            lock (rootLock)
            {
                var additional = string.Empty;
                if (!string.IsNullOrEmpty(PostgresDatabase))
                {
                    additional = $";Database={PostgresDatabase}";
                }

                if (!string.IsNullOrEmpty(PostgresAdditionalOptions))
                {
                    additional += $";{PostgresAdditionalOptions}";
                }

                return $"Host={PostgresHost};Username={PostgresUserName};Password={PostgresPassword}{additional}";
            }
        }
    }
}
