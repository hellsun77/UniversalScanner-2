using System;
using System.Data.SQLite;
using System.IO;

namespace UniversalScanner.AI
{
    public sealed class DeviceBrandOverride
    {
        public string ForcedBrand { get; set; }
        public string OriginalVendor { get; set; }
        public string OriginalProtocol { get; set; }
    }

    public sealed class FingerprintPersistenceResult
    {
        public long ObservationId { get; set; }
        public bool Created { get; set; }
    }

    public sealed class LearnedFingerprintMatch
    {
        public long ObservationId { get; set; }
        public string Vendor { get; set; }
        public string Model { get; set; }
        public string Protocol { get; set; }
        public double MatchScore { get; set; }
        public string MatchReason { get; set; }
    }

    public sealed class LearnedBrandSuggestion
    {
        public string Brand { get; set; }
        public double Confidence { get; set; }
        public string Reason { get; set; }
        public long SourceObservationId { get; set; }
    }

    public sealed class KnowledgeBase
    {
        public string DatabasePath { get; private set; }
        public bool IsLoaded { get; private set; }
        public DateTime? LoadedAtUtc { get; private set; }

        public void Create(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Database path is required.", nameof(databasePath));

            string fullPath = Path.GetFullPath(databasePath);
            string directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(fullPath))
                File.Delete(fullPath);

            SQLiteConnection.CreateFile(fullPath);

            using (SQLiteConnection connection = new SQLiteConnection(BuildConnectionString(fullPath)))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = GetCreateSchemaSql();
                    command.ExecuteNonQuery();
                }
            }

            Load(fullPath);
        }

        public void Load(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Database path is required.", nameof(databasePath));

            string fullPath = Path.GetFullPath(databasePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("AI knowledge database was not found.", fullPath);

            DatabasePath = fullPath;
            LoadedAtUtc = DateTime.UtcNow;
            IsLoaded = true;
            EnsureSchema();
        }

        public void Unload()
        {
            DatabasePath = null;
            LoadedAtUtc = null;
            IsLoaded = false;
        }

        public long SaveFingerprintObservation(DeviceFingerprint fingerprint)
        {
            if (!IsLoaded)
                throw new InvalidOperationException("AI knowledge database is not loaded.");

            if (fingerprint == null)
                throw new ArgumentNullException(nameof(fingerprint));

            using (SQLiteConnection connection = new SQLiteConnection(BuildConnectionString(DatabasePath)))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
INSERT INTO fingerprint_observations
    (ip_address, mac_address, vendor, model, protocol, onvif_profile, oui_vendor, notes)
VALUES
    (@ip_address, @mac_address, @vendor, @model, @protocol, @onvif_profile, @oui_vendor, @notes);

SELECT last_insert_rowid();";
                    command.Parameters.AddWithValue("@ip_address", ToDbValue(fingerprint.IpAddress));
                    command.Parameters.AddWithValue("@mac_address", ToDbValue(fingerprint.MacAddress));
                    command.Parameters.AddWithValue("@vendor", ToDbValue(fingerprint.Vendor));
                    command.Parameters.AddWithValue("@model", ToDbValue(fingerprint.Model));
                    command.Parameters.AddWithValue("@protocol", ToDbValue(fingerprint.Protocol));
                    command.Parameters.AddWithValue("@onvif_profile", ToDbValue(fingerprint.OnvifProfile));
                    command.Parameters.AddWithValue("@oui_vendor", ToDbValue(GetSignalValue(fingerprint, "oui_vendor")));
                    command.Parameters.AddWithValue("@notes", ToDbValue(BuildObservationNotes(fingerprint)));

                    return Convert.ToInt64(command.ExecuteScalar());
                }
            }
        }

        public FingerprintPersistenceResult SaveOrUpdateFingerprintObservation(DeviceFingerprint fingerprint)
        {
            if (!IsLoaded)
                throw new InvalidOperationException("AI knowledge database is not loaded.");

            if (fingerprint == null)
                throw new ArgumentNullException(nameof(fingerprint));

            long existingId = FindLatestFingerprintObservationId(fingerprint);
            if (existingId == 0)
            {
                return new FingerprintPersistenceResult
                {
                    ObservationId = SaveFingerprintObservation(fingerprint),
                    Created = true
                };
            }

            using (SQLiteConnection connection = new SQLiteConnection(BuildConnectionString(DatabasePath)))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
UPDATE fingerprint_observations
SET
    ip_address = @ip_address,
    mac_address = @mac_address,
    vendor = @vendor,
    model = @model,
    protocol = @protocol,
    onvif_profile = @onvif_profile,
    oui_vendor = @oui_vendor,
    captured_at_utc = CURRENT_TIMESTAMP,
    notes = @notes
WHERE id = @id;";
                    command.Parameters.AddWithValue("@id", existingId);
                    command.Parameters.AddWithValue("@ip_address", ToDbValue(fingerprint.IpAddress));
                    command.Parameters.AddWithValue("@mac_address", ToDbValue(fingerprint.MacAddress));
                    command.Parameters.AddWithValue("@vendor", ToDbValue(fingerprint.Vendor));
                    command.Parameters.AddWithValue("@model", ToDbValue(fingerprint.Model));
                    command.Parameters.AddWithValue("@protocol", ToDbValue(fingerprint.Protocol));
                    command.Parameters.AddWithValue("@onvif_profile", ToDbValue(fingerprint.OnvifProfile));
                    command.Parameters.AddWithValue("@oui_vendor", ToDbValue(GetSignalValue(fingerprint, "oui_vendor")));
                    command.Parameters.AddWithValue("@notes", ToDbValue(BuildObservationNotes(fingerprint)));
                    command.ExecuteNonQuery();
                }
            }

            return new FingerprintPersistenceResult
            {
                ObservationId = existingId,
                Created = false
            };
        }

        public long FindLatestFingerprintObservationId(DeviceFingerprint fingerprint)
        {
            if (!IsLoaded || fingerprint == null)
                return 0;

            using (SQLiteConnection connection = new SQLiteConnection(BuildConnectionString(DatabasePath)))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT id
FROM fingerprint_observations
WHERE
    (
        @mac_address IS NOT NULL
        AND replace(replace(replace(upper(ifnull(mac_address, '')), '-', ''), ':', ''), ' ', '') =
            replace(replace(replace(upper(ifnull(@mac_address, '')), '-', ''), ':', ''), ' ', '')
    )
    OR
    (
        (@mac_address IS NULL OR mac_address IS NULL)
        AND ip_address = @ip_address
        AND ifnull(vendor, '') = ifnull(@vendor, '')
        AND ifnull(model, '') = ifnull(@model, '')
        AND ifnull(protocol, '') = ifnull(@protocol, '')
    )
ORDER BY id DESC
LIMIT 1;";
                    command.Parameters.AddWithValue("@ip_address", ToDbValue(fingerprint.IpAddress));
                    command.Parameters.AddWithValue("@mac_address", ToDbValue(fingerprint.MacAddress));
                    command.Parameters.AddWithValue("@vendor", ToDbValue(fingerprint.Vendor));
                    command.Parameters.AddWithValue("@model", ToDbValue(fingerprint.Model));
                    command.Parameters.AddWithValue("@protocol", ToDbValue(fingerprint.Protocol));

                    object result = command.ExecuteScalar();
                    return result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result);
                }
            }
        }

        public LearnedFingerprintMatch FindBestFingerprintMatch(DeviceFingerprint fingerprint)
        {
            if (!IsLoaded || fingerprint == null)
                return null;

            LearnedFingerprintMatch bestMatch = null;

            using (SQLiteConnection connection = new SQLiteConnection(BuildConnectionString(DatabasePath)))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT id, ip_address, mac_address, vendor, model, protocol, onvif_profile
FROM fingerprint_observations
ORDER BY id DESC
LIMIT 500;";

                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            double score = ScoreObservationMatch(fingerprint, reader);
                            if (score <= 0.0)
                                continue;

                            if (bestMatch == null || score > bestMatch.MatchScore)
                            {
                                bestMatch = new LearnedFingerprintMatch
                                {
                                    ObservationId = Convert.ToInt64(reader["id"]),
                                    Vendor = Convert.ToString(reader["vendor"]),
                                    Model = Convert.ToString(reader["model"]),
                                    Protocol = Convert.ToString(reader["protocol"]),
                                    MatchScore = score,
                                    MatchReason = BuildMatchReason(fingerprint, reader)
                                };
                            }
                        }
                    }
                }
            }

            return bestMatch != null && bestMatch.MatchScore >= 0.45 ? bestMatch : null;
        }

        public LearnedBrandSuggestion SuggestBrand(DeviceFingerprint fingerprint)
        {
            if (!IsLoaded || fingerprint == null)
                return null;

            LearnedBrandSuggestion bestSuggestion = null;

            using (SQLiteConnection connection = new SQLiteConnection(BuildConnectionString(DatabasePath)))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT id, vendor, model, protocol, onvif_profile, oui_vendor, notes
FROM fingerprint_observations
WHERE vendor IS NOT NULL AND trim(vendor) <> ''
ORDER BY id DESC
LIMIT 1000;";

                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string vendor = Convert.ToString(reader["vendor"]);
                            if (IsGenericVendor(vendor))
                                continue;

                            double score = ScoreBrandSuggestion(fingerprint, reader);
                            if (score <= 0.0)
                                continue;

                            if (bestSuggestion == null || score > bestSuggestion.Confidence)
                            {
                                bestSuggestion = new LearnedBrandSuggestion
                                {
                                    Brand = vendor.Trim(),
                                    Confidence = score,
                                    Reason = BuildBrandSuggestionReason(fingerprint, reader),
                                    SourceObservationId = Convert.ToInt64(reader["id"])
                                };
                            }
                        }
                    }
                }
            }

            return bestSuggestion != null && bestSuggestion.Confidence >= 0.45 ? bestSuggestion : null;
        }

        public DeviceBrandOverride GetBrandOverride(DeviceFingerprint fingerprint)
        {
            if (!IsLoaded || fingerprint == null)
                return null;

            string matchType;
            string matchKey;
            if (!TryBuildOverrideMatchKey(fingerprint, out matchType, out matchKey))
                return null;

            using (SQLiteConnection connection = new SQLiteConnection(BuildConnectionString(DatabasePath)))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT forced_brand, original_vendor, original_protocol
FROM brand_overrides
WHERE match_type = @match_type AND match_key = @match_key
LIMIT 1;";
                    command.Parameters.AddWithValue("@match_type", matchType);
                    command.Parameters.AddWithValue("@match_key", matchKey);

                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;

                        return new DeviceBrandOverride
                        {
                            ForcedBrand = Convert.ToString(reader["forced_brand"]),
                            OriginalVendor = Convert.ToString(reader["original_vendor"]),
                            OriginalProtocol = Convert.ToString(reader["original_protocol"])
                        };
                    }
                }
            }
        }

        public void SaveBrandOverride(DeviceFingerprint fingerprint, string forcedBrand)
        {
            SaveBrandOverride(fingerprint, forcedBrand, fingerprint == null ? "" : fingerprint.Vendor, fingerprint == null ? "" : fingerprint.Protocol);
        }

        public void SaveBrandOverride(DeviceFingerprint fingerprint, string forcedBrand, string originalVendor, string originalProtocol)
        {
            if (!IsLoaded)
                throw new InvalidOperationException("AI knowledge database is not loaded.");

            if (fingerprint == null)
                throw new ArgumentNullException(nameof(fingerprint));

            if (string.IsNullOrWhiteSpace(forcedBrand))
                throw new ArgumentException("Forced brand is required.", nameof(forcedBrand));

            string matchType;
            string matchKey;
            if (!TryBuildOverrideMatchKey(fingerprint, out matchType, out matchKey))
                throw new InvalidOperationException("The selected device does not have enough identity information for a brand override.");

            DeviceBrandOverride existingOverride = GetBrandOverride(fingerprint);
            if (existingOverride != null)
            {
                if (!string.IsNullOrWhiteSpace(existingOverride.OriginalVendor))
                    originalVendor = existingOverride.OriginalVendor;

                if (!string.IsNullOrWhiteSpace(existingOverride.OriginalProtocol))
                    originalProtocol = existingOverride.OriginalProtocol;
            }

            using (SQLiteConnection connection = new SQLiteConnection(BuildConnectionString(DatabasePath)))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
INSERT OR REPLACE INTO brand_overrides
    (match_type, match_key, ip_address, mac_address, forced_brand, original_vendor, original_protocol, updated_at_utc)
VALUES
    (@match_type, @match_key, @ip_address, @mac_address, @forced_brand, @original_vendor, @original_protocol, CURRENT_TIMESTAMP);";
                    command.Parameters.AddWithValue("@match_type", matchType);
                    command.Parameters.AddWithValue("@match_key", matchKey);
                    command.Parameters.AddWithValue("@ip_address", ToDbValue(fingerprint.IpAddress));
                    command.Parameters.AddWithValue("@mac_address", ToDbValue(fingerprint.MacAddress));
                    command.Parameters.AddWithValue("@forced_brand", forcedBrand.Trim());
                    command.Parameters.AddWithValue("@original_vendor", ToDbValue(RemoveOverrideDisplaySuffix(originalVendor)));
                    command.Parameters.AddWithValue("@original_protocol", ToDbValue(RemoveOverrideDisplaySuffix(originalProtocol)));
                    command.ExecuteNonQuery();
                }
            }
        }

        public void ClearBrandOverride(DeviceFingerprint fingerprint)
        {
            if (!IsLoaded || fingerprint == null)
                return;

            string matchType;
            string matchKey;
            if (!TryBuildOverrideMatchKey(fingerprint, out matchType, out matchKey))
                return;

            using (SQLiteConnection connection = new SQLiteConnection(BuildConnectionString(DatabasePath)))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM brand_overrides WHERE match_type = @match_type AND match_key = @match_key;";
                    command.Parameters.AddWithValue("@match_type", matchType);
                    command.Parameters.AddWithValue("@match_key", matchKey);
                    command.ExecuteNonQuery();
                }
            }
        }

        private void EnsureSchema()
        {
            using (SQLiteConnection connection = new SQLiteConnection(BuildConnectionString(DatabasePath)))
            {
                connection.Open();
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = GetMigrationSchemaSql();
                    command.ExecuteNonQuery();
                }

                EnsureColumn(connection, "brand_overrides", "original_vendor", "TEXT");
                EnsureColumn(connection, "brand_overrides", "original_protocol", "TEXT");
                EnsureColumn(connection, "fingerprint_observations", "oui_vendor", "TEXT");
            }
        }

        private static void EnsureColumn(SQLiteConnection connection, string tableName, string columnName, string columnType)
        {
            if (ColumnExists(connection, tableName, columnName))
                return;

            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "ALTER TABLE " + tableName + " ADD COLUMN " + columnName + " " + columnType + ";";
                command.ExecuteNonQuery();
            }
        }

        private static bool ColumnExists(SQLiteConnection connection, string tableName, string columnName)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(" + tableName + ");";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string name = Convert.ToString(reader["name"]);
                        if (name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }

            return false;
        }

        private static bool TryBuildOverrideMatchKey(DeviceFingerprint fingerprint, out string matchType, out string matchKey)
        {
            string mac = NormalizeIdentityValue(fingerprint.MacAddress);
            if (!string.IsNullOrWhiteSpace(mac))
            {
                matchType = "mac";
                matchKey = mac;
                return true;
            }

            string ip = NormalizeIdentityValue(fingerprint.IpAddress);
            if (!string.IsNullOrWhiteSpace(ip))
            {
                matchType = "ip";
                matchKey = ip;
                return true;
            }

            matchType = "";
            matchKey = "";
            return false;
        }

        private static string NormalizeIdentityValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? ""
                : value.Trim().ToUpperInvariant();
        }

        private static string NormalizeMacIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value.Trim()
                .Replace("-", "")
                .Replace(":", "")
                .Replace(" ", "")
                .ToUpperInvariant();
        }

        private static double ScoreObservationMatch(DeviceFingerprint fingerprint, SQLiteDataReader reader)
        {
            double score = 0.0;

            string fingerprintMac = NormalizeMacIdentity(fingerprint.MacAddress);
            string savedMac = NormalizeMacIdentity(Convert.ToString(reader["mac_address"]));
            if (!string.IsNullOrWhiteSpace(fingerprintMac) &&
                !string.IsNullOrWhiteSpace(savedMac) &&
                fingerprintMac == savedMac)
            {
                score += 0.60;
            }

            if (TextMatches(fingerprint.Model, Convert.ToString(reader["model"])))
                score += 0.18;

            if (TextMatches(fingerprint.Vendor, Convert.ToString(reader["vendor"])))
                score += 0.10;

            if (TextMatches(fingerprint.Protocol, Convert.ToString(reader["protocol"])))
                score += 0.08;

            if (TextMatches(fingerprint.OnvifProfile, Convert.ToString(reader["onvif_profile"])))
                score += 0.04;

            return score > 1.0 ? 1.0 : score;
        }

        private static string BuildMatchReason(DeviceFingerprint fingerprint, SQLiteDataReader reader)
        {
            System.Collections.Generic.List<string> reasons = new System.Collections.Generic.List<string>();

            string fingerprintMac = NormalizeMacIdentity(fingerprint.MacAddress);
            string savedMac = NormalizeMacIdentity(Convert.ToString(reader["mac_address"]));
            if (!string.IsNullOrWhiteSpace(fingerprintMac) &&
                !string.IsNullOrWhiteSpace(savedMac) &&
                fingerprintMac == savedMac)
                reasons.Add("MAC");

            if (TextMatches(fingerprint.Model, Convert.ToString(reader["model"])))
                reasons.Add("model");

            if (TextMatches(fingerprint.Vendor, Convert.ToString(reader["vendor"])))
                reasons.Add("vendor");

            if (TextMatches(fingerprint.Protocol, Convert.ToString(reader["protocol"])))
                reasons.Add("protocol");

            if (TextMatches(fingerprint.OnvifProfile, Convert.ToString(reader["onvif_profile"])))
                reasons.Add("ONVIF");

            return reasons.Count == 0 ? "none" : string.Join(", ", reasons);
        }

        private static bool TextMatches(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            return left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static double ScoreBrandSuggestion(DeviceFingerprint fingerprint, SQLiteDataReader reader)
        {
            double score = 0.0;

            string fingerprintOui = NormalizeText(GetSignalValue(fingerprint, "oui_vendor"));
            string savedOui = NormalizeText(Convert.ToString(reader["oui_vendor"]));
            if (string.IsNullOrWhiteSpace(savedOui))
                savedOui = NormalizeText(ParseNoteValue(Convert.ToString(reader["notes"]), "oui_vendor"));

            if (!string.IsNullOrWhiteSpace(fingerprintOui) &&
                !string.IsNullOrWhiteSpace(savedOui) &&
                fingerprintOui == savedOui)
                score += 0.55;

            string fingerprintModel = NormalizeText(fingerprint.Model);
            string savedModel = NormalizeText(Convert.ToString(reader["model"]));
            if (!string.IsNullOrWhiteSpace(fingerprintModel) &&
                !string.IsNullOrWhiteSpace(savedModel))
            {
                if (fingerprintModel == savedModel)
                    score += 0.30;
                else if (GetModelFamily(fingerprintModel) == GetModelFamily(savedModel))
                    score += 0.18;
            }

            string fingerprintProtocol = NormalizeText(fingerprint.Protocol);
            string savedProtocol = NormalizeText(Convert.ToString(reader["protocol"]));
            if (!IsGenericVendor(fingerprintProtocol) &&
                !string.IsNullOrWhiteSpace(fingerprintProtocol) &&
                fingerprintProtocol == savedProtocol)
                score += 0.10;

            if (TextMatches(fingerprint.OnvifProfile, Convert.ToString(reader["onvif_profile"])))
                score += 0.05;

            return score > 1.0 ? 1.0 : score;
        }

        private static string BuildBrandSuggestionReason(DeviceFingerprint fingerprint, SQLiteDataReader reader)
        {
            System.Collections.Generic.List<string> reasons = new System.Collections.Generic.List<string>();

            string fingerprintOui = NormalizeText(GetSignalValue(fingerprint, "oui_vendor"));
            string savedOui = NormalizeText(Convert.ToString(reader["oui_vendor"]));
            if (string.IsNullOrWhiteSpace(savedOui))
                savedOui = NormalizeText(ParseNoteValue(Convert.ToString(reader["notes"]), "oui_vendor"));

            if (!string.IsNullOrWhiteSpace(fingerprintOui) &&
                !string.IsNullOrWhiteSpace(savedOui) &&
                fingerprintOui == savedOui)
                reasons.Add("same OUI vendor");

            string fingerprintModel = NormalizeText(fingerprint.Model);
            string savedModel = NormalizeText(Convert.ToString(reader["model"]));
            if (!string.IsNullOrWhiteSpace(fingerprintModel) &&
                !string.IsNullOrWhiteSpace(savedModel))
            {
                if (fingerprintModel == savedModel)
                    reasons.Add("same model");
                else if (GetModelFamily(fingerprintModel) == GetModelFamily(savedModel))
                    reasons.Add("similar model family");
            }

            string fingerprintProtocol = NormalizeText(fingerprint.Protocol);
            string savedProtocol = NormalizeText(Convert.ToString(reader["protocol"]));
            if (!IsGenericVendor(fingerprintProtocol) &&
                !string.IsNullOrWhiteSpace(fingerprintProtocol) &&
                fingerprintProtocol == savedProtocol)
                reasons.Add("same protocol");

            if (TextMatches(fingerprint.OnvifProfile, Convert.ToString(reader["onvif_profile"])))
                reasons.Add("same ONVIF profile");

            return reasons.Count == 0 ? "learned fingerprint similarity" : string.Join(", ", reasons);
        }

        public static bool IsGenericVendor(string value)
        {
            string normalized = NormalizeText(value);
            return string.IsNullOrWhiteSpace(normalized) ||
                normalized == "UNKNOWN" ||
                normalized == "DISCOVERY" ||
                normalized == "WSDISCOVERY" ||
                normalized == "SSDP" ||
                normalized == "VMS/WINDOWS" ||
                normalized == "GOOGLE";
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();
        }

        private static string GetModelFamily(string normalizedModel)
        {
            if (string.IsNullOrWhiteSpace(normalizedModel))
                return "";

            int dashIndex = normalizedModel.IndexOf('-');
            if (dashIndex > 2)
                return normalizedModel.Substring(0, dashIndex);

            return normalizedModel.Length <= 6 ? normalizedModel : normalizedModel.Substring(0, 6);
        }

        private static string GetSignalValue(DeviceFingerprint fingerprint, string signalName)
        {
            if (fingerprint == null || fingerprint.Signals == null || string.IsNullOrWhiteSpace(signalName))
                return "";

            string value;
            return fingerprint.Signals.TryGetValue(signalName, out value) ? value : "";
        }

        private static string ParseNoteValue(string notes, string key)
        {
            if (string.IsNullOrWhiteSpace(notes) || string.IsNullOrWhiteSpace(key))
                return "";

            string prefix = key.Trim() + "=";
            string[] parts = notes.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(prefix.Length).Trim();
            }

            return "";
        }

        private static object ToDbValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value.Trim();
        }

        private static string RemoveOverrideDisplaySuffix(string value)
        {
            const string suffix = " [AI Override]";
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string trimmed = value.Trim();
            return trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(0, trimmed.Length - suffix.Length)
                : trimmed;
        }

        private static string BuildObservationNotes(DeviceFingerprint fingerprint)
        {
            if (fingerprint.Signals == null || fingerprint.Signals.Count == 0)
                return "";

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            foreach (System.Collections.Generic.KeyValuePair<string, string> signal in fingerprint.Signals)
            {
                if (builder.Length > 0)
                    builder.Append("; ");

                builder.Append(signal.Key);
                builder.Append("=");
                builder.Append(signal.Value);
            }

            return builder.ToString();
        }

        private static string BuildConnectionString(string databasePath)
        {
            SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder
            {
                DataSource = databasePath,
                Version = 3
            };

            return builder.ToString();
        }

        private static string GetCreateSchemaSql()
        {
            return @"
PRAGMA user_version = 1;

CREATE TABLE metadata (
    key TEXT PRIMARY KEY NOT NULL,
    value TEXT NOT NULL
);

CREATE TABLE device_signatures (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    vendor TEXT,
    model TEXT,
    protocol TEXT,
    firmware TEXT,
    onvif_profile TEXT,
    signal_name TEXT,
    signal_value TEXT,
    confidence REAL DEFAULT 0,
    created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE fingerprint_observations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    ip_address TEXT,
    mac_address TEXT,
    vendor TEXT,
    model TEXT,
    protocol TEXT,
    onvif_profile TEXT,
    oui_vendor TEXT,
    captured_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    notes TEXT
);

CREATE TABLE classification_rules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    rule_name TEXT NOT NULL,
    vendor TEXT,
    model TEXT,
    pattern TEXT NOT NULL,
    confidence REAL NOT NULL DEFAULT 0,
    enabled INTEGER NOT NULL DEFAULT 1,
    created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE brand_overrides (
    match_type TEXT NOT NULL,
    match_key TEXT NOT NULL,
    ip_address TEXT,
    mac_address TEXT,
    forced_brand TEXT NOT NULL,
    original_vendor TEXT,
    original_protocol TEXT,
    created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (match_type, match_key)
);

INSERT INTO metadata(key, value) VALUES ('schema_name', 'UniversalScanner AI Knowledge Database');
INSERT INTO metadata(key, value) VALUES ('schema_version', '1');
";
        }

        private static string GetMigrationSchemaSql()
        {
            return @"
CREATE TABLE IF NOT EXISTS brand_overrides (
    match_type TEXT NOT NULL,
    match_key TEXT NOT NULL,
    ip_address TEXT,
    mac_address TEXT,
    forced_brand TEXT NOT NULL,
    original_vendor TEXT,
    original_protocol TEXT,
    created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (match_type, match_key)
);
";
        }
    }
}
