using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using UniversalScanner.AI;

namespace UniversalScanner
{
    public partial class ScannerWindow
    {
        private ToolStripMenuItem normalViewToolStripMenuItem;
        private ToolStripMenuItem advancedViewToolStripMenuItem;
        private ToolStripMenuItem createAiKnowledgeDatabaseToolStripMenuItem;
        private ToolStripMenuItem loadAiKnowledgeDatabaseToolStripMenuItem;
        private ToolStripMenuItem unloadAiKnowledgeDatabaseToolStripMenuItem;
        private ToolStripMenuItem treatSelectedDeviceAsBrandMenuItem;
        private Panel advancedViewPanel;
        private Label advancedViewTitleLabel;
        private Label advancedViewStatusLabel;
        private Label advancedViewDatabaseLabel;
        private Label advancedSelectedDeviceLabel;
        private Label advancedConfidenceLabel;
        private Label advancedSaveStatusLabel;
        private Button saveSelectedFingerprintButton;
        private long selectedFingerprintObservationId;
        private string selectedBrandOverride;
        private readonly HashSet<DataGridViewRow> pendingAdvancedRows = new HashSet<DataGridViewRow>();
        private readonly HashSet<DataGridViewRow> pendingBrandOverrideRows = new HashSet<DataGridViewRow>();
        private bool advancedRowsRefreshQueued;
        private bool advancedRowsRefreshProcessing;
        private readonly object aiKnowledgeBaseWriteLock = new object();
        private bool applyingQueuedBrandOverride;
        private int advancedLearnedTotal;
        private int advancedUpdatedTotal;
        private int advancedSkippedTotal;
        private int advancedAutoLearnGeneration;
        private bool advancedAutoLearnSummaryVisible;
        private bool advancedExplicitSaveStatus;
        private DateTime advancedExplicitSaveStatusUtc;
        private const string BrandOverrideDisplaySuffix = " [AI Override]";
        private readonly KnowledgeBase aiKnowledgeBase = new KnowledgeBase();
        private readonly DeviceClassifier aiDeviceClassifier = new DeviceClassifier();

        private void InitializeAdvancedArchitecture()
        {
            InitializeAdvancedToolsMenu();
            BuildAdvancedViewPanel();
            AddTreatAsBrandRightClickMenu();
            HookAdvancedBrandOverrideRefresh();
            UpdateAiKnowledgeDatabaseStatus();
            ApplyViewVisibility();
        }

        private void InitializeAdvancedToolsMenu()
        {
            normalViewToolStripMenuItem = new ToolStripMenuItem();
            normalViewToolStripMenuItem.CheckOnClick = true;
            normalViewToolStripMenuItem.Checked = true;
            normalViewToolStripMenuItem.Click += (sender, e) => ApplyViewVisibility();

            advancedViewToolStripMenuItem = new ToolStripMenuItem();
            advancedViewToolStripMenuItem.CheckOnClick = true;
            advancedViewToolStripMenuItem.Checked = false;
            advancedViewToolStripMenuItem.Enabled = true;
            advancedViewToolStripMenuItem.Click += (sender, e) => ApplyViewVisibility();

            createAiKnowledgeDatabaseToolStripMenuItem = new ToolStripMenuItem("Create/Load AI Knowledge Database");
            createAiKnowledgeDatabaseToolStripMenuItem.Click += createAiKnowledgeDatabaseToolStripMenuItem_Click;

            loadAiKnowledgeDatabaseToolStripMenuItem = new ToolStripMenuItem("Load AI Knowledge Database");
            loadAiKnowledgeDatabaseToolStripMenuItem.Click += loadAiKnowledgeDatabaseToolStripMenuItem_Click;
            loadAiKnowledgeDatabaseToolStripMenuItem.Visible = false;

            unloadAiKnowledgeDatabaseToolStripMenuItem = new ToolStripMenuItem("Unload AI Knowledge Database");
            unloadAiKnowledgeDatabaseToolStripMenuItem.Enabled = false;
            unloadAiKnowledgeDatabaseToolStripMenuItem.Click += unloadAiKnowledgeDatabaseToolStripMenuItem_Click;

            int insertIndex = 0;
            toolsToolStripMenuItem.DropDownItems.Insert(insertIndex++, normalViewToolStripMenuItem);
            toolsToolStripMenuItem.DropDownItems.Insert(insertIndex++, advancedViewToolStripMenuItem);
            toolsToolStripMenuItem.DropDownItems.Insert(insertIndex++, new ToolStripSeparator());
            toolsToolStripMenuItem.DropDownItems.Insert(insertIndex++, createAiKnowledgeDatabaseToolStripMenuItem);
            toolsToolStripMenuItem.DropDownItems.Insert(insertIndex++, unloadAiKnowledgeDatabaseToolStripMenuItem);
            toolsToolStripMenuItem.DropDownItems.Insert(insertIndex, new ToolStripSeparator());

            if (toolsToolStripMenuItem.DropDown is ToolStripDropDownMenu dropDownMenu)
                dropDownMenu.ShowCheckMargin = true;

            UpdateViewMenuText();
        }

        private void BuildAdvancedViewPanel()
        {
            AppVisualPalette palette = AppVisualTheme.Current;
            advancedViewPanel = new Panel
            {
                BackColor = palette.Surface,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            advancedViewTitleLabel = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Regular),
                ForeColor = palette.AccentText,
                Text = "Advanced Device Analysis"
            };

            advancedViewStatusLabel = new Label
            {
                AutoSize = false,
                Font = AppVisualTheme.BodyFont,
                ForeColor = palette.TextPrimary,
                Text = "Fingerprint, classifier, confidence, and protocol analysis scaffolding is ready."
            };

            advancedViewDatabaseLabel = new Label
            {
                AutoSize = false,
                Font = AppVisualTheme.CaptionFont,
                ForeColor = palette.TextSecondary,
                Text = "AI knowledge database: not loaded"
            };

            advancedSelectedDeviceLabel = new Label
            {
                AutoSize = false,
                Font = AppVisualTheme.CaptionFont,
                ForeColor = palette.TextPrimary,
                Text = "Selected device: none"
            };

            advancedConfidenceLabel = new Label
            {
                AutoSize = false,
                Font = AppVisualTheme.CaptionFont,
                ForeColor = palette.TextPrimary,
                Text = "Classification confidence: 0%"
            };

            saveSelectedFingerprintButton = new Button
            {
                Text = "Save Fingerprint",
                Enabled = false,
                BackColor = palette.Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = AppVisualTheme.ButtonFont,
                Cursor = Cursors.Hand
            };
            saveSelectedFingerprintButton.FlatAppearance.BorderColor = palette.Accent;
            saveSelectedFingerprintButton.FlatAppearance.MouseOverBackColor = palette.AccentHover;
            saveSelectedFingerprintButton.FlatAppearance.MouseDownBackColor = palette.AccentPressed;
            saveSelectedFingerprintButton.Click += saveSelectedFingerprintButton_Click;

            advancedSaveStatusLabel = new Label
            {
                AutoSize = false,
                Font = AppVisualTheme.CaptionFont,
                ForeColor = palette.TextSecondary,
                Text = "Fingerprint not saved"
            };

            advancedViewPanel.Controls.Add(advancedViewTitleLabel);
            advancedViewPanel.Controls.Add(advancedViewStatusLabel);
            advancedViewPanel.Controls.Add(advancedViewDatabaseLabel);
            advancedViewPanel.Controls.Add(advancedSelectedDeviceLabel);
            advancedViewPanel.Controls.Add(advancedConfidenceLabel);
            advancedViewPanel.Controls.Add(saveSelectedFingerprintButton);
            advancedViewPanel.Controls.Add(advancedSaveStatusLabel);
            Controls.Add(advancedViewPanel);
            Controls.SetChildIndex(advancedViewPanel, 0);
            UpdateAdvancedSelectedDevicePanel();
        }

        private void RefreshAdvancedViewTheme()
        {
            if (advancedViewPanel == null)
                return;

            AppVisualPalette palette = AppVisualTheme.Current;
            advancedViewPanel.BackColor = palette.Surface;
            advancedViewTitleLabel.ForeColor = palette.AccentText;
            advancedViewStatusLabel.ForeColor = palette.TextPrimary;
            advancedViewDatabaseLabel.ForeColor = palette.TextSecondary;
            advancedSelectedDeviceLabel.ForeColor = palette.TextPrimary;
            advancedConfidenceLabel.ForeColor = palette.TextPrimary;
            advancedSaveStatusLabel.ForeColor = palette.TextSecondary;
            saveSelectedFingerprintButton.BackColor = palette.Accent;
            saveSelectedFingerprintButton.ForeColor = Color.White;
            saveSelectedFingerprintButton.FlatAppearance.BorderColor = palette.Accent;
            saveSelectedFingerprintButton.FlatAppearance.MouseOverBackColor = palette.AccentHover;
            saveSelectedFingerprintButton.FlatAppearance.MouseDownBackColor = palette.AccentPressed;
            advancedViewPanel.Invalidate(true);
        }

        private void AddTreatAsBrandRightClickMenu()
        {
            if (rightClickMenu == null || treatSelectedDeviceAsBrandMenuItem != null)
                return;

            treatSelectedDeviceAsBrandMenuItem = new ToolStripMenuItem("Treat as Brand");

            foreach (string brand in BrandEngineMapper.GetSupportedBrands())
            {
                string selectedBrand = brand;
                ToolStripMenuItem brandItem = new ToolStripMenuItem(selectedBrand);
                brandItem.Click += (sender, e) => TreatSelectedDeviceAsBrand(selectedBrand);
                treatSelectedDeviceAsBrandMenuItem.DropDownItems.Add(brandItem);
            }

            treatSelectedDeviceAsBrandMenuItem.DropDownItems.Add(new ToolStripSeparator());
            treatSelectedDeviceAsBrandMenuItem.DropDownItems.Add("Clear Brand Override", null, clearBrandOverrideToolStripMenuItem_Click);

            int insertIndex = Math.Min(5, rightClickMenu.Items.Count);
            rightClickMenu.Items.Insert(insertIndex, treatSelectedDeviceAsBrandMenuItem);
        }

        private void ApplyViewVisibility()
        {
            if (normalViewToolStripMenuItem == null || advancedViewToolStripMenuItem == null)
                return;

            if (!normalViewToolStripMenuItem.Checked && !advancedViewToolStripMenuItem.Checked)
                normalViewToolStripMenuItem.Checked = true;

            UpdateViewMenuText();
            UpdateAdvancedToolsAvailability();
            dataGridView1.Visible = normalViewToolStripMenuItem.Checked;

            if (advancedViewPanel != null)
            {
                advancedViewPanel.Visible = advancedViewToolStripMenuItem.Checked;
                if (!advancedViewPanel.Visible)
                    advancedViewPanel.SendToBack();
            }

            UpdateMainDesignLayout();
        }

        private void UpdateViewMenuText()
        {
            if (normalViewToolStripMenuItem != null)
                normalViewToolStripMenuItem.Text = "Normal View";

            if (advancedViewToolStripMenuItem != null)
                advancedViewToolStripMenuItem.Text = "Advanced View";
        }

        private void UpdateAdvancedToolsAvailability()
        {
            bool advancedEnabled = advancedViewToolStripMenuItem != null && advancedViewToolStripMenuItem.Checked;

            if (createAiKnowledgeDatabaseToolStripMenuItem != null)
                createAiKnowledgeDatabaseToolStripMenuItem.Enabled = advancedEnabled;

            if (loadAiKnowledgeDatabaseToolStripMenuItem != null)
                loadAiKnowledgeDatabaseToolStripMenuItem.Enabled = advancedEnabled;

            if (unloadAiKnowledgeDatabaseToolStripMenuItem != null)
                unloadAiKnowledgeDatabaseToolStripMenuItem.Enabled = advancedEnabled && aiKnowledgeBase.IsLoaded;

            if (saveSelectedFingerprintButton != null)
                saveSelectedFingerprintButton.Enabled = advancedEnabled && aiKnowledgeBase.IsLoaded && GetAdvancedSelectedDeviceRow() != null && selectedFingerprintObservationId == 0;

            if (treatSelectedDeviceAsBrandMenuItem != null)
                treatSelectedDeviceAsBrandMenuItem.Enabled = aiKnowledgeBase.IsLoaded && GetAdvancedSelectedDeviceRow() != null;
        }

        private void UpdateAdvancedViewLayout(int contentTop, int width, int statusHeight)
        {
            if (advancedViewPanel == null || !advancedViewPanel.Visible)
                return;

            bool normalVisible = dataGridView1 != null && dataGridView1.Visible;
            int availableHeight = Math.Max(120, ClientSize.Height - contentTop - statusHeight);
            int panelHeight = normalVisible ? Math.Min(190, Math.Max(130, availableHeight / 3)) : availableHeight;
            int panelTop = normalVisible ? ClientSize.Height - statusHeight - panelHeight : contentTop;

            advancedViewPanel.SetBounds(0, panelTop, width, panelHeight);
            advancedViewTitleLabel.SetBounds(18, 14, Math.Max(200, width - 36), 24);
            advancedViewStatusLabel.SetBounds(18, 44, Math.Max(200, width - 36), 22);
            advancedViewDatabaseLabel.SetBounds(18, 72, Math.Max(200, width - 36), 22);
            advancedSelectedDeviceLabel.SetBounds(18, 102, Math.Max(200, width - 36), 22);
            advancedConfidenceLabel.SetBounds(18, 130, Math.Max(200, width - 36), 22);
            saveSelectedFingerprintButton.SetBounds(18, 158, 130, 26);
            advancedSaveStatusLabel.SetBounds(164, 160, Math.Max(200, width - 182), 22);
        }

        private int GetAdvancedViewReservedHeight(int contentTop, int statusHeight)
        {
            if (advancedViewPanel == null || !advancedViewPanel.Visible || dataGridView1 == null || !dataGridView1.Visible)
                return 0;

            int availableHeight = Math.Max(120, ClientSize.Height - contentTop - statusHeight);
            return Math.Min(190, Math.Max(130, availableHeight / 3));
        }

        private void loadAiKnowledgeDatabaseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TryLoadOrCreateAiKnowledgeDatabase(true);
        }

        private void createAiKnowledgeDatabaseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (TryLoadOrCreateAiKnowledgeDatabase(true))
            {
                _ = ShowStatusAsync("AI knowledge database is ready.", 3000);
            }
        }

        private void unloadAiKnowledgeDatabaseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            aiKnowledgeBase.Unload();
            UpdateAiKnowledgeDatabaseStatus();
            _ = ShowStatusAsync("AI knowledge database unloaded.", 2500);
        }

        private void UpdateAiKnowledgeDatabaseStatus()
        {
            bool loaded = aiKnowledgeBase.IsLoaded;
            ResetAdvancedAutoLearnTotals();

            UpdateAdvancedToolsAvailability();

            if (advancedViewDatabaseLabel != null)
            {
                advancedViewDatabaseLabel.Text = loaded
                    ? "AI knowledge database: " + aiKnowledgeBase.DatabasePath
                    : "AI knowledge database: not loaded";
            }

            QueueAllAdvancedRowsRefresh();
            UpdateAdvancedSelectedDevicePanel();
        }

        private void ResetAdvancedAutoLearnTotals()
        {
            advancedLearnedTotal = 0;
            advancedUpdatedTotal = 0;
            advancedSkippedTotal = 0;
            advancedAutoLearnGeneration++;
            pendingAdvancedRows.Clear();
            pendingBrandOverrideRows.Clear();
            if (advancedAutoLearnSummaryVisible && advancedSaveStatusLabel != null)
                advancedSaveStatusLabel.Text = "";
            advancedAutoLearnSummaryVisible = false;
            advancedExplicitSaveStatus = false;
        }

        private void SetAdvancedSaveStatus(string message)
        {
            if (advancedSaveStatusLabel == null)
                return;

            advancedSaveStatusLabel.Text = message;
            advancedExplicitSaveStatus = true;
            advancedExplicitSaveStatusUtc = DateTime.UtcNow;
            advancedAutoLearnSummaryVisible = false;
        }

        private void SetAutoLearnSummary(string message)
        {
            if (advancedSaveStatusLabel == null)
                return;

            if (advancedExplicitSaveStatus && DateTime.UtcNow - advancedExplicitSaveStatusUtc < TimeSpan.FromSeconds(5))
                return;

            advancedSaveStatusLabel.Text = message;
            advancedExplicitSaveStatus = false;
            advancedAutoLearnSummaryVisible = true;
        }

        private void LoadOrCreateDefaultAiKnowledgeDatabase()
        {
            string databasePath = GetDefaultAiKnowledgeDatabasePath();

            if (File.Exists(databasePath))
                aiKnowledgeBase.Load(databasePath);
            else
                aiKnowledgeBase.Create(databasePath);

            UpdateAiKnowledgeDatabaseStatus();
        }

        private bool TryLoadOrCreateAiKnowledgeDatabase(bool showMessage)
        {
            try
            {
                LoadOrCreateDefaultAiKnowledgeDatabase();
                return true;
            }
            catch (Exception ex)
            {
                aiKnowledgeBase.Unload();
                UpdateAiKnowledgeDatabaseStatus();

                if (showMessage)
                {
                    MessageBox.Show(this,
                        "The AI knowledge database could not be prepared.\r\n\r\n" +
                        ex.Message +
                        "\r\n\r\nThe scanner will continue without AI learning.",
                        "AI Knowledge Database",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return false;
            }
        }

        private string GetDefaultAiKnowledgeDatabasePath()
        {
            return Path.Combine(Application.StartupPath, "UniversalScanner_AI_Knowledge.db");
        }

        private void UpdateAdvancedSelectedDevicePanel()
        {
            if (advancedSelectedDeviceLabel == null || advancedConfidenceLabel == null || dataGridView1 == null)
                return;

            DataGridViewRow selectedRow = GetAdvancedSelectedDeviceRow();
            if (selectedRow == null)
            {
                selectedFingerprintObservationId = 0;
                selectedBrandOverride = "";
                advancedSelectedDeviceLabel.Text = "Selected device: none";
                advancedConfidenceLabel.Text = "Classification confidence: 0%";
                if (advancedSaveStatusLabel != null)
                {
                    advancedSaveStatusLabel.Text = "Select one device to save its fingerprint.";
                    advancedExplicitSaveStatus = false;
                    advancedAutoLearnSummaryVisible = false;
                }
                UpdateAdvancedToolsAvailability();
                return;
            }

            DeviceFingerprint fingerprint = BuildSelectedDeviceFingerprint(selectedRow);
            ApplyStoredBrandOverrideToRow(fingerprint, selectedRow);
            DeviceClassification classification = aiDeviceClassifier.Classify(fingerprint, aiKnowledgeBase);
            bool autoLearned = AutoLearnFingerprintIfReady(fingerprint);
            advancedSelectedDeviceLabel.Text =
                "Selected device: " +
                SafeDisplay(fingerprint.IpAddress) +
                " | " + SafeDisplay(fingerprint.Vendor) +
                " | " + SafeDisplay(fingerprint.Model) +
                " | " + SafeDisplay(fingerprint.MacAddress) +
                " | " + SafeDisplay(fingerprint.OnvifProfile);

            advancedConfidenceLabel.Text =
                "Classification confidence: " +
                Math.Round(classification.Confidence * 100.0) +
                "% | Knowledge DB: " +
                (classification.UsedKnowledgeBase ? "loaded" : "not loaded") +
                BuildLearnedMatchText(classification) +
                BuildBrandSuggestionText(classification);

            if (!autoLearned)
                UpdateFingerprintSaveStatus(fingerprint);

            UpdateAdvancedToolsAvailability();
        }

        private void saveSelectedFingerprintButton_Click(object sender, EventArgs e)
        {
            DataGridViewRow selectedRow = GetAdvancedSelectedDeviceRow();
            if (selectedRow == null)
                return;

            try
            {
                DeviceFingerprint fingerprint = BuildSelectedDeviceFingerprint(selectedRow);
                long id = SaveFingerprintObservationSafely(fingerprint);
                selectedFingerprintObservationId = id;

                SetAdvancedSaveStatus("Fingerprint saved to DB as observation #" + id + ".");

                UpdateAdvancedToolsAvailability();
                _ = ShowStatusAsync("Selected device fingerprint saved.", 2500);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "The selected fingerprint could not be saved.\r\n\r\n" + ex.Message,
                    "AI Knowledge Database",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static string BuildLearnedMatchText(DeviceClassification classification)
        {
            if (classification == null || classification.LearnedMatch == null)
                return "";

            LearnedFingerprintMatch match = classification.LearnedMatch;
            return " | Learned match: observation #" +
                match.ObservationId +
                " (" +
                Math.Round(match.MatchScore * 100.0) +
                "%, " +
                match.MatchReason +
                ")";
        }

        private static string BuildBrandSuggestionText(DeviceClassification classification)
        {
            if (classification == null || classification.BrandSuggestion == null)
                return "";

            LearnedBrandSuggestion suggestion = classification.BrandSuggestion;
            return " | AI suggestion: probably " +
                suggestion.Brand +
                " (" +
                Math.Round(suggestion.Confidence * 100.0) +
                "%, " +
                suggestion.Reason +
                ")";
        }

        private void UpdateFingerprintSaveStatus(DeviceFingerprint fingerprint)
        {
            if (advancedSaveStatusLabel == null)
                return;

            selectedFingerprintObservationId = 0;

            if (!aiKnowledgeBase.IsLoaded)
            {
                SetAdvancedSaveStatus("Load the AI knowledge database before saving fingerprints.");
                return;
            }

            long existingId = aiKnowledgeBase.FindLatestFingerprintObservationId(fingerprint);
            selectedFingerprintObservationId = existingId;
            SetAdvancedSaveStatus(existingId > 0
                ? "Fingerprint already saved in DB as observation #" + existingId + "."
                : "Fingerprint not saved yet.");
        }

        private bool AutoLearnFingerprintIfReady(DeviceFingerprint fingerprint)
        {
            selectedFingerprintObservationId = 0;

            bool advancedEnabled = advancedViewToolStripMenuItem != null && advancedViewToolStripMenuItem.Checked;
            if (!advancedEnabled || !aiKnowledgeBase.IsLoaded || fingerprint == null)
                return false;

            try
            {
                FingerprintPersistenceResult result = SaveOrUpdateFingerprintObservationSafely(fingerprint);
                selectedFingerprintObservationId = result.ObservationId;

                if (advancedSaveStatusLabel != null)
                {
                    SetAdvancedSaveStatus(result.Created
                        ? BuildAutoLearnStatus("AI learned this fingerprint", result.ObservationId)
                        : BuildAutoLearnStatus("AI updated saved fingerprint", result.ObservationId));
                }

                return true;
            }
            catch (Exception ex)
            {
                SetAdvancedSaveStatus("AI could not update the knowledge DB: " + ex.Message);

                return false;
            }
        }

        private void ApplyStoredBrandOverrideToRow(DeviceFingerprint fingerprint, DataGridViewRow row)
        {
            selectedBrandOverride = "";

            if (!aiKnowledgeBase.IsLoaded || fingerprint == null || row == null)
                return;

            DeviceBrandOverride brandOverride = aiKnowledgeBase.GetBrandOverride(fingerprint);
            if (brandOverride == null || string.IsNullOrWhiteSpace(brandOverride.ForcedBrand))
                return;

            ApplyBrandOverrideToRowAndFingerprint(row, fingerprint, brandOverride.ForcedBrand);
            selectedBrandOverride = BrandEngineMapper.NormalizeVendor(brandOverride.ForcedBrand);
        }

        private void ApplyAiBrandOverridesToSelectedRowsForAction()
        {
            if (!aiKnowledgeBase.IsLoaded)
                return;

            foreach (DataGridViewRow row in GetSelectedDeviceRows())
                ApplyAiBrandOverrideToActionRow(row);

            if (dataGridView1 != null)
                dataGridView1.Refresh();
        }

        private void ApplyAiBrandOverrideToActionRow(DataGridViewRow row)
        {
            if (!aiKnowledgeBase.IsLoaded || row == null || row.IsNewRow)
                return;

            DeviceFingerprint fingerprint = BuildSelectedDeviceFingerprint(row);
            ApplyStoredBrandOverrideToRow(fingerprint, row);
        }

        private void HookAdvancedBrandOverrideRefresh()
        {
            if (dataGridView1 == null)
                return;

            dataGridView1.RowsAdded += (sender, e) =>
            {
                int end = Math.Min(dataGridView1.Rows.Count, e.RowIndex + e.RowCount);
                for (int index = Math.Max(0, e.RowIndex); index < end; index++)
                    QueueAdvancedRowRefresh(dataGridView1.Rows[index], true);
            };

        }

        private void QueueAdvancedRowRefresh(DataGridViewRow row, bool applyBrandOverride)
        {
            if (applyingQueuedBrandOverride || !aiKnowledgeBase.IsLoaded || dataGridView1 == null || row == null || row.IsNewRow)
                return;
            if (!dataGridView1.IsHandleCreated || dataGridView1.IsDisposed)
                return;

            pendingAdvancedRows.Add(row);
            if (applyBrandOverride)
                pendingBrandOverrideRows.Add(row);
            ScheduleAdvancedRowsRefresh();
        }

        private void QueueAllAdvancedRowsRefresh()
        {
            if (!aiKnowledgeBase.IsLoaded || dataGridView1 == null || !dataGridView1.IsHandleCreated || dataGridView1.IsDisposed)
                return;

            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row != null && !row.IsNewRow)
                {
                    pendingAdvancedRows.Add(row);
                    pendingBrandOverrideRows.Add(row);
                }
            }
            ScheduleAdvancedRowsRefresh();
        }

        private void ScheduleAdvancedRowsRefresh()
        {
            if (advancedRowsRefreshQueued || advancedRowsRefreshProcessing ||
                dataGridView1 == null || dataGridView1.IsDisposed || !dataGridView1.IsHandleCreated)
                return;

            advancedRowsRefreshQueued = true;
            try
            {
                BeginInvoke(new Action(ProcessPendingAdvancedRows));
            }
            catch (InvalidOperationException)
            {
                advancedRowsRefreshQueued = false;
            }
        }

        private async void ProcessPendingAdvancedRows()
        {
            advancedRowsRefreshQueued = false;
            if (advancedRowsRefreshProcessing)
                return;
            if (!aiKnowledgeBase.IsLoaded || dataGridView1 == null || dataGridView1.IsDisposed)
            {
                pendingAdvancedRows.Clear();
                pendingBrandOverrideRows.Clear();
                return;
            }

            advancedRowsRefreshProcessing = true;
            try
            {
            int batchGeneration = advancedAutoLearnGeneration;
            DataGridViewRow[] rows = pendingAdvancedRows.Take(32).ToArray();
            foreach (DataGridViewRow row in rows)
                pendingAdvancedRows.Remove(row);
            int learned = 0;
            int updated = 0;
            int skipped = 0;
            List<DeviceFingerprint> fingerprints = new List<DeviceFingerprint>();
            foreach (DataGridViewRow row in rows)
            {
                try
                {
                if (row == null || row.IsNewRow || row.DataGridView != dataGridView1)
                {
                    pendingBrandOverrideRows.Remove(row);
                    continue;
                }

                if (pendingBrandOverrideRows.Remove(row))
                {
                    applyingQueuedBrandOverride = true;
                    try
                    {
                        ApplyAiBrandOverrideToActionRow(row);
                    }
                    catch
                    {
                        skipped++;
                    }
                    finally
                    {
                        applyingQueuedBrandOverride = false;
                    }
                }
                DeviceFingerprint fingerprint = BuildSelectedDeviceFingerprint(row);
                if (HasLearnableFingerprintIdentity(fingerprint))
                    fingerprints.Add(fingerprint);
                else
                    skipped++;
                }
                catch
                {
                    skipped++;
                }
            }

            if (fingerprints.Count > 0)
            {
                int[] persistenceCounts = await Task.Run(() =>
                {
                    int created = 0;
                    int changed = 0;
                    int failed = 0;
                    foreach (DeviceFingerprint fingerprint in fingerprints)
                    {
                        try
                        {
                            if (!aiKnowledgeBase.IsLoaded)
                            {
                                failed++;
                                continue;
                            }

                            FingerprintPersistenceResult result = SaveOrUpdateFingerprintObservationSafely(fingerprint);
                            if (result.Created)
                                created++;
                            else
                                changed++;
                        }
                        catch
                        {
                            failed++;
                        }
                    }
                    return new[] { created, changed, failed };
                });
                learned += persistenceCounts[0];
                updated += persistenceCounts[1];
                skipped += persistenceCounts[2];
            }

            if (batchGeneration == advancedAutoLearnGeneration)
            {
                advancedLearnedTotal += learned;
                advancedUpdatedTotal += updated;
                advancedSkippedTotal += skipped;
            }
            else
            {
                learned = 0;
                updated = 0;
                skipped = 0;
            }
            try
            {
            if (learned + updated > 0 &&
                advancedViewToolStripMenuItem != null &&
                    advancedViewToolStripMenuItem.Checked &&
                    advancedSaveStatusLabel != null)
                {
                    SetAutoLearnSummary(
                        "AI auto-learned scan rows. New: " + advancedLearnedTotal +
                        ", updated: " + advancedUpdatedTotal +
                        (advancedSkippedTotal > 0 ? ", skipped: " + advancedSkippedTotal : "") + ".");
                }

                if (rows.Length > 0)
                    dataGridView1.Invalidate();
                RefreshSelectedFingerprintSavedState();
                UpdateAdvancedToolsAvailability();
            }
            catch (Exception ex)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Warn, "Advanced row refresh UI update failed: " + ex.Message);
            }
            }
            catch (Exception ex)
            {
                Logger.getInstance().WriteLine(Logger.DebugLevel.Warn, "Advanced row batch failed: " + ex.Message);
            }
            finally
            {
                advancedRowsRefreshProcessing = false;
                if (pendingAdvancedRows.Count > 0 && !advancedRowsRefreshQueued)
                    ScheduleAdvancedRowsRefresh();
            }
        }

        private FingerprintPersistenceResult SaveOrUpdateFingerprintObservationSafely(DeviceFingerprint fingerprint)
        {
            lock (aiKnowledgeBaseWriteLock)
                return aiKnowledgeBase.SaveOrUpdateFingerprintObservation(fingerprint);
        }

        private long SaveFingerprintObservationSafely(DeviceFingerprint fingerprint)
        {
            lock (aiKnowledgeBaseWriteLock)
                return aiKnowledgeBase.SaveFingerprintObservation(fingerprint);
        }

        private void SaveBrandOverrideSafely(DeviceFingerprint fingerprint, string brand)
        {
            lock (aiKnowledgeBaseWriteLock)
                aiKnowledgeBase.SaveBrandOverride(fingerprint, brand, fingerprint.Vendor, fingerprint.Protocol);
        }

        private void ClearBrandOverrideSafely(DeviceFingerprint fingerprint)
        {
            lock (aiKnowledgeBaseWriteLock)
                aiKnowledgeBase.ClearBrandOverride(fingerprint);
        }

        private void RefreshSelectedFingerprintSavedState()
        {
            selectedFingerprintObservationId = 0;

            if (!aiKnowledgeBase.IsLoaded)
                return;

            DataGridViewRow selectedRow = GetAdvancedSelectedDeviceRow();
            if (selectedRow == null)
                return;

            DeviceFingerprint fingerprint = BuildSelectedDeviceFingerprint(selectedRow);
            selectedFingerprintObservationId = aiKnowledgeBase.FindLatestFingerprintObservationId(fingerprint);
        }

        private static bool HasLearnableFingerprintIdentity(DeviceFingerprint fingerprint)
        {
            if (fingerprint == null)
                return false;

            bool hasDeviceIdentity =
                !string.IsNullOrWhiteSpace(fingerprint.MacAddress) ||
                !string.IsNullOrWhiteSpace(fingerprint.IpAddress);

            bool hasDeviceDetails =
                !string.IsNullOrWhiteSpace(fingerprint.Vendor) ||
                !string.IsNullOrWhiteSpace(fingerprint.Model) ||
                !string.IsNullOrWhiteSpace(fingerprint.Protocol) ||
                !string.IsNullOrWhiteSpace(fingerprint.OnvifProfile);

            return hasDeviceIdentity && hasDeviceDetails;
        }

        private string BuildAutoLearnStatus(string action, long observationId)
        {
            if (!string.IsNullOrWhiteSpace(selectedBrandOverride))
                return action + " as observation #" + observationId + " with brand override: " + selectedBrandOverride + ".";

            return action + " as observation #" + observationId + ".";
        }

        private void TreatSelectedDeviceAsBrand(string brand)
        {
            DataGridViewRow selectedRow = GetAdvancedSelectedDeviceRow();
            if (selectedRow == null)
                return;

            try
            {
                DeviceFingerprint fingerprint = BuildSelectedDeviceFingerprint(selectedRow);
                SaveBrandOverrideSafely(fingerprint, brand);
                ApplyBrandOverrideToRowAndFingerprint(selectedRow, fingerprint, brand);
                SaveOrUpdateFingerprintObservationSafely(fingerprint);

                SetAdvancedSaveStatus("AI brand override saved: treat this device as " + brand + ".");

                UpdateAdvancedSelectedDevicePanel();
                _ = ShowStatusAsync("AI will treat this device as " + brand + ".", 3000);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "The brand override could not be saved.\r\n\r\n" + ex.Message,
                    "AI Brand Override",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void clearBrandOverrideToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DataGridViewRow selectedRow = GetAdvancedSelectedDeviceRow();
            if (selectedRow == null)
                return;

            try
            {
                DeviceFingerprint fingerprint = BuildSelectedDeviceFingerprint(selectedRow);
                DeviceBrandOverride brandOverride = aiKnowledgeBase.GetBrandOverride(fingerprint);
                ClearBrandOverrideSafely(fingerprint);
                RestoreRowBrandAfterClearingOverride(selectedRow, brandOverride);
                SaveOrUpdateFingerprintObservationSafely(BuildSelectedDeviceFingerprint(selectedRow));

                SetAdvancedSaveStatus("AI brand override cleared for this device.");

                UpdateAdvancedSelectedDevicePanel();
                _ = ShowStatusAsync("AI brand override cleared.", 2500);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "The brand override could not be cleared.\r\n\r\n" + ex.Message,
                    "AI Brand Override",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void ApplyBrandOverrideToRowAndFingerprint(DataGridViewRow row, DeviceFingerprint fingerprint, string brand)
        {
            if (row == null || fingerprint == null || string.IsNullOrWhiteSpace(brand))
                return;

            string normalizedBrand = BrandEngineMapper.NormalizeVendor(brand);
            if (string.IsNullOrWhiteSpace(normalizedBrand))
                normalizedBrand = brand.Trim();

            bool wasApplyingQueuedBrandOverride = applyingQueuedBrandOverride;
            applyingQueuedBrandOverride = true;
            try
            {
                SetCellText(row, Columns.Vendor, normalizedBrand + BrandOverrideDisplaySuffix);
                SetCellText(row, Columns.Protocol, normalizedBrand);
                MarkBrandOverrideRow(row, normalizedBrand);
                fingerprint.Vendor = normalizedBrand;
                fingerprint.Protocol = normalizedBrand;
                fingerprint.AddSignal("brand_override", normalizedBrand);
            }
            finally
            {
                applyingQueuedBrandOverride = wasApplyingQueuedBrandOverride;
            }
        }

        private void MarkBrandOverrideRow(DataGridViewRow row, string brand)
        {
            if (row == null)
                return;

            row.DefaultCellStyle.BackColor = AppVisualTheme.Current.IsDark
                ? Color.FromArgb(73, 61, 27)
                : Color.FromArgb(255, 248, 220);
            row.DefaultCellStyle.ForeColor = AppVisualTheme.Current.IsDark
                ? Color.FromArgb(255, 225, 151)
                : Color.FromArgb(84, 57, 0);

            string tooltip = "AI brand override active: treated as " + brand + ".";
            SetCellToolTip(row, Columns.Vendor, tooltip);
            SetCellToolTip(row, Columns.Protocol, tooltip);
        }

        private void ClearBrandOverrideMarkerFromRow(DataGridViewRow row)
        {
            if (row == null)
                return;

            string vendor = GetCellText(row, Columns.Vendor);
            if (vendor.EndsWith(BrandOverrideDisplaySuffix, StringComparison.OrdinalIgnoreCase))
                SetCellText(row, Columns.Vendor, vendor.Substring(0, vendor.Length - BrandOverrideDisplaySuffix.Length));

            row.DefaultCellStyle.BackColor = Color.Empty;
            ApplyDiscoveryRowStyle(row);
            SetCellToolTip(row, Columns.Vendor, "");
            SetCellToolTip(row, Columns.Protocol, "");
        }

        private void RestoreRowBrandAfterClearingOverride(DataGridViewRow row, DeviceBrandOverride brandOverride)
        {
            if (row == null)
                return;

            string restoredVendor = brandOverride == null ? "" : brandOverride.OriginalVendor;
            string restoredProtocol = brandOverride == null ? "" : brandOverride.OriginalProtocol;

            if (!string.IsNullOrWhiteSpace(restoredVendor) || !string.IsNullOrWhiteSpace(restoredProtocol))
            {
                if (string.IsNullOrWhiteSpace(restoredVendor))
                    restoredVendor = restoredProtocol;

                if (string.IsNullOrWhiteSpace(restoredProtocol))
                    restoredProtocol = restoredVendor;

                SetCellText(row, Columns.Vendor, RemoveOverrideDisplaySuffix(restoredVendor));
                SetCellText(row, Columns.Protocol, RemoveOverrideDisplaySuffix(restoredProtocol));
            }
            else
            {
                string restoredBrand = GetBestDetectedBrandAfterOverride(row);
                if (!string.IsNullOrWhiteSpace(restoredBrand))
                {
                    SetCellText(row, Columns.Vendor, restoredBrand);
                    SetCellText(row, Columns.Protocol, restoredBrand);
                }
                else
                {
                    ClearBrandOverrideMarkerFromRow(row);
                }
            }

            row.DefaultCellStyle.BackColor = Color.Empty;
            ApplyDiscoveryRowStyle(row);
            SetCellToolTip(row, Columns.Vendor, "");
            SetCellToolTip(row, Columns.Protocol, "");
        }

        private string GetBestDetectedBrandAfterOverride(DataGridViewRow row)
        {
            string ouiBrand = BrandEngineMapper.NormalizeVendor(GetCellText(row, Columns.MACVendor));
            if (IsKnownBrand(ouiBrand))
                return ouiBrand;

            string modelBrand = GuessBrandFromModel(GetCellText(row, Columns.Type));
            if (IsKnownBrand(modelBrand))
                return modelBrand;

            string uniqueIdBrand = BrandEngineMapper.NormalizeVendor(GetCellText(row, Columns.UniqueId));
            if (IsKnownBrand(uniqueIdBrand))
                return uniqueIdBrand;

            string vendor = GetCellText(row, Columns.Vendor);
            if (vendor.EndsWith(BrandOverrideDisplaySuffix, StringComparison.OrdinalIgnoreCase))
                vendor = vendor.Substring(0, vendor.Length - BrandOverrideDisplaySuffix.Length);

            return "";
        }

        private static string RemoveOverrideDisplaySuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string trimmed = value.Trim();
            return trimmed.EndsWith(BrandOverrideDisplaySuffix, StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(0, trimmed.Length - BrandOverrideDisplaySuffix.Length)
                : trimmed;
        }

        private static bool IsKnownBrand(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            foreach (string brand in BrandEngineMapper.GetSupportedBrands())
            {
                if (brand.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string GuessBrandFromModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                return "";

            string value = model.Trim();
            if (value.StartsWith("DS-", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("iDS-", StringComparison.OrdinalIgnoreCase))
                return "Hikvision";

            return BrandEngineMapper.NormalizeVendor(value);
        }

        private DataGridViewRow GetAdvancedSelectedDeviceRow()
        {
            if (dataGridView1 == null)
                return null;

            if (dataGridView1.SelectedRows.Count == 1 && !dataGridView1.SelectedRows[0].IsNewRow)
                return dataGridView1.SelectedRows[0];

            if (dataGridView1.CurrentRow != null && !dataGridView1.CurrentRow.IsNewRow)
                return dataGridView1.CurrentRow;

            if (dataGridView1.SelectedCells.Count > 0)
            {
                int rowIndex = dataGridView1.SelectedCells[0].RowIndex;
                if (rowIndex >= 0 && rowIndex < dataGridView1.Rows.Count && !dataGridView1.Rows[rowIndex].IsNewRow)
                    return dataGridView1.Rows[rowIndex];
            }

            return null;
        }

        private DeviceFingerprint BuildSelectedDeviceFingerprint(DataGridViewRow row)
        {
            DeviceFingerprint fingerprint = new DeviceFingerprint
            {
                Vendor = GetFingerprintCellText(row, Columns.Vendor),
                Protocol = GetFingerprintCellText(row, Columns.Protocol),
                FirmwareVersion = GetCellText(row, Columns.Version),
                IpAddress = GetCellText(row, Columns.IPAddress),
                MacAddress = GetCellText(row, Columns.MAC),
                Model = GetCellText(row, Columns.Type),
                OnvifProfile = GetCellText(row, Columns.OnvifProfile)
            };

            fingerprint.AddSignal("activation", GetCellText(row, Columns.Activation));
            fingerprint.AddSignal("oui_vendor", GetCellText(row, Columns.MACVendor));
            fingerprint.AddSignal("unique_id", GetCellText(row, Columns.UniqueId));
            fingerprint.AddSignal("ping", GetCellText(row, Columns.Ping));

            return fingerprint;
        }

        private string GetCellText(DataGridViewRow row, Columns column)
        {
            if (row == null)
                return "";

            string columnName = ColumnNames[(int)column];
            if (row.DataGridView != null && row.DataGridView.Columns.Contains(columnName))
            {
                object namedValue = row.Cells[columnName].Value;
                return namedValue == null ? "" : namedValue.ToString();
            }

            int fallbackIndex = (int)column;
            if (fallbackIndex < 0 || fallbackIndex >= row.Cells.Count)
                return "";

            return row.Cells[fallbackIndex].Value == null ? "" : row.Cells[fallbackIndex].Value.ToString();
        }

        private string GetFingerprintCellText(DataGridViewRow row, Columns column)
        {
            string value = GetCellText(row, column);
            if (column == Columns.Vendor && value.EndsWith(BrandOverrideDisplaySuffix, StringComparison.OrdinalIgnoreCase))
                return value.Substring(0, value.Length - BrandOverrideDisplaySuffix.Length);

            return value;
        }

        private void SetCellText(DataGridViewRow row, Columns column, string value)
        {
            if (row == null)
                return;

            string columnName = ColumnNames[(int)column];
            if (row.DataGridView != null && row.DataGridView.Columns.Contains(columnName))
            {
                row.Cells[columnName].Value = value ?? "";
                return;
            }

            int fallbackIndex = (int)column;
            if (fallbackIndex < 0 || fallbackIndex >= row.Cells.Count)
                return;

            row.Cells[fallbackIndex].Value = value ?? "";
        }

        private void SetCellToolTip(DataGridViewRow row, Columns column, string value)
        {
            if (row == null)
                return;

            string columnName = ColumnNames[(int)column];
            if (row.DataGridView != null && row.DataGridView.Columns.Contains(columnName))
            {
                row.Cells[columnName].ToolTipText = value ?? "";
                return;
            }

            int fallbackIndex = (int)column;
            if (fallbackIndex < 0 || fallbackIndex >= row.Cells.Count)
                return;

            row.Cells[fallbackIndex].ToolTipText = value ?? "";
        }

        private static string SafeDisplay(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
        }
    }
}
