using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace WallhavenScreensaver;

internal sealed class ConfigForm : Form
{
    private readonly ComboBox _sorting = new();
    private readonly ComboBox _category = new();
    private readonly TextBox _query = new();
    private readonly ComboBox _contentFilter = new();
    private readonly Label _contentFilterDescription = new();
    private readonly NumericUpDown _interval = new();
    private readonly ComboBox _transition = new();
    private readonly ComboBox _scaleMode = new();
    private readonly ComboBox _multiMonitor = new();
    private readonly CheckBox _displayAware = new();
    private readonly NumericUpDown _cacheTargetFiles = new();
    private readonly NumericUpDown _cacheMaxFiles = new();
    private readonly NumericUpDown _cacheMaxMiB = new();
    private readonly NumericUpDown _historyMaxIds = new();
    private readonly AppSettings _settings;

    public ConfigForm()
    {
        _settings = SettingsStore.Load();

        Text = "Wallhaven Screensaver — Options";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 820);
        Size = new Size(780, 1020);
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 6,
            AutoScroll = true
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildWallhavenGroup(), 0, 1);
        root.Controls.Add(BuildDisplayGroup(), 0, 2);
        root.Controls.Add(BuildCacheGroup(), 0, 3);
        root.Controls.Add(BuildFooterNote(), 0, 4);
        root.Controls.Add(BuildButtons(), 0, 5);

        Controls.Add(root);
        LoadSettingsIntoControls();

        _contentFilter.SelectedIndexChanged +=
            (_, _) => UpdateFilterDescription();

        UpdateFilterDescription();
    }

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 78,
            Margin = new Padding(0, 0, 0, 12)
        };

        var title = new Label
        {
            Text = "Wallhaven Screensaver",
            Font = new Font("Segoe UI Semibold", 18F),
            AutoSize = true,
            Location = new Point(0, 0)
        };

        var subtitle = new Label
        {
            Text = "Économiseur d’écran Windows alimenté par l’API publique SFW de Wallhaven.",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Location = new Point(2, 42)
        };

        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);
        return panel;
    }

    private Control BuildWallhavenGroup()
    {
        var group = CreateGroup("Source Wallhaven", 260);
        var table = CreateTwoColumnTable();

        ConfigureCombo(
            _sorting,
            ["Aléatoire", "Tendance", "Populaires", "Nouveaux"]);

        ConfigureCombo(
            _category,
            ["Toutes", "Général", "Anime", "Personnes"]);

        ConfigureCombo(
            _contentFilter,
            ["Standard", "Reduced", "Strict"]);

        _query.Width = 360;
        _query.MaxLength = 512;
        _query.PlaceholderText = "Ex. +nature -people (optionnel)";

        _contentFilterDescription.AutoSize = true;
        _contentFilterDescription.MaximumSize = new Size(440, 0);
        _contentFilterDescription.ForeColor = SystemColors.GrayText;

        AddRow(table, 0, "Sélection :", _sorting);
        AddRow(table, 1, "Catégorie :", _category);
        AddRow(table, 2, "Requête :", _query);
        AddRow(table, 3, "Filtrage :", _contentFilter);

        table.Controls.Add(_contentFilterDescription, 1, 4);

        var sfw = new Label
        {
            Text = "Wallhaven reste toujours interrogé avec purity=100. Reduced/Strict ajoutent un filtrage local des métadonnées/tags.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 8, 3, 3)
        };

        table.Controls.Add(sfw, 1, 5);
        group.Controls.Add(table);
        return group;
    }

    private Control BuildDisplayGroup()
    {
        var group = CreateGroup("Affichage", 250);
        var table = CreateTwoColumnTable();

        _interval.Minimum = 1;
        _interval.Maximum = 120;
        _interval.Width = 90;

        ConfigureCombo(
            _transition,
            ["Aucune", "Fondu — 500 ms", "Fondu — 750 ms", "Fondu — 1 s", "Fondu — 2 s"]);

        ConfigureCombo(
            _scaleMode,
            ["Remplir l’écran (crop)", "Ajuster (bandes noires)"]);

        ConfigureCombo(
            _multiMonitor,
            ["Même image sur tous les écrans", "Image différente par écran"]);

        var intervalPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        intervalPanel.Controls.Add(_interval);
        intervalPanel.Controls.Add(
            new Label
            {
                Text = "minute(s)",
                AutoSize = true,
                Margin = new Padding(8, 6, 0, 0)
            });

        _displayAware.Text =
            "Adapter la requête à la résolution et au ratio de l’écran";
        _displayAware.AutoSize = true;

        AddRow(table, 0, "Changer l’image :", intervalPanel);
        AddRow(table, 1, "Transition :", _transition);
        AddRow(table, 2, "Mise à l’échelle :", _scaleMode);
        AddRow(table, 3, "Multi-écrans :", _multiMonitor);
        table.Controls.Add(_displayAware, 1, 4);

        group.Controls.Add(table);
        return group;
    }

    private Control BuildCacheGroup()
    {
        var group = CreateGroup("Cache et anti-répétition", 300);
        var table = CreateTwoColumnTable();

        _cacheTargetFiles.Minimum = 8;
        _cacheTargetFiles.Maximum = 20;
        _cacheTargetFiles.Width = 90;

        _cacheMaxFiles.Minimum = 8;
        _cacheMaxFiles.Maximum = 200;
        _cacheMaxFiles.Width = 90;

        _cacheMaxMiB.Minimum = 100;
        _cacheMaxMiB.Maximum = 5000;
        _cacheMaxMiB.Increment = 100;
        _cacheMaxMiB.Width = 90;

        _historyMaxIds.Minimum = 1000;
        _historyMaxIds.Maximum = 20000;
        _historyMaxIds.Increment = 500;
        _historyMaxIds.Width = 100;

        AddRow(table, 0, "Pool prêt :", _cacheTargetFiles);
        AddRow(table, 1, "Limite fichiers :", _cacheMaxFiles);

        var cacheSizePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        cacheSizePanel.Controls.Add(_cacheMaxMiB);
        cacheSizePanel.Controls.Add(
            new Label
            {
                Text = "MiB",
                AutoSize = true,
                Margin = new Padding(8, 6, 0, 0)
            });

        AddRow(table, 2, "Taille max :", cacheSizePanel);
        AddRow(table, 3, "Historique long :", _historyMaxIds);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(3, 8, 3, 3)
        };

        var clearCache = new Button
        {
            Text = "Vider le cache",
            AutoSize = true
        };

        var resetHistory = new Button
        {
            Text = "Réinitialiser l’historique",
            AutoSize = true
        };

        var diagnostics = new Button
        {
            Text = "Diagnostic",
            AutoSize = true
        };

        clearCache.Click += (_, _) => ClearCache();
        resetHistory.Click += (_, _) => ResetHistory();
        diagnostics.Click += (_, _) => ShowDiagnostics();

        actions.Controls.Add(clearCache);
        actions.Controls.Add(resetHistory);
        actions.Controls.Add(diagnostics);

        table.Controls.Add(actions, 1, 4);
        group.Controls.Add(table);
        return group;
    }

    private Control BuildFooterNote()
    {
        return new Label
        {
            Text =
                "L’historique est indépendant du cache : vider les images ne réinitialise jamais l’anti-répétition. " +
                "Le filtre local repose sur les métadonnées Wallhaven, pas sur une reconnaissance d’image.",
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(4, 14, 4, 8)
        };
    }

    private Control BuildButtons()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };

        var save = new Button
        {
            Text = "Enregistrer",
            AutoSize = true,
            Padding = new Padding(12, 5, 12, 5)
        };

        var cancel = new Button
        {
            Text = "Annuler",
            AutoSize = true,
            Padding = new Padding(12, 5, 12, 5)
        };

        var test = new Button
        {
            Text = "Tester",
            AutoSize = true,
            Padding = new Padding(12, 5, 12, 5)
        };

        save.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => Close();
        test.Click += (_, _) => TestScreensaver();

        AcceptButton = save;
        CancelButton = cancel;

        panel.Controls.Add(save);
        panel.Controls.Add(cancel);
        panel.Controls.Add(test);
        return panel;
    }

    private void LoadSettingsIntoControls()
    {
        _sorting.SelectedIndex = _settings.Sorting switch
        {
            WallhavenSorting.Trending => 1,
            WallhavenSorting.Popular => 2,
            WallhavenSorting.Newest => 3,
            _ => 0
        };

        _category.SelectedIndex = _settings.Category switch
        {
            WallhavenCategory.General => 1,
            WallhavenCategory.Anime => 2,
            WallhavenCategory.People => 3,
            _ => 0
        };

        _query.Text = _settings.Query;

        _contentFilter.SelectedIndex = _settings.ContentFilter switch
        {
            ContentFilterMode.Standard => 0,
            ContentFilterMode.Strict => 2,
            _ => 1
        };

        _interval.Value = _settings.IntervalMinutes;

        _transition.SelectedIndex = _settings.FadeMilliseconds switch
        {
            0 => 0,
            <= 500 => 1,
            <= 750 => 2,
            <= 1000 => 3,
            _ => 4
        };

        _scaleMode.SelectedIndex =
            _settings.ScaleMode == ImageScaleMode.Fill ? 0 : 1;

        _multiMonitor.SelectedIndex =
            _settings.MultiMonitorMode == MultiMonitorMode.SameImage ? 0 : 1;

        _displayAware.Checked = _settings.DisplayAwareFiltering;
        _cacheTargetFiles.Value = Math.Clamp(_settings.CacheTargetFiles, 8, 20);
        _cacheMaxFiles.Value = Math.Clamp(_settings.CacheMaxFiles, 8, 200);
        _cacheMaxMiB.Value = Math.Clamp(_settings.CacheMaxMiB, 100, 5000);
        _historyMaxIds.Value = Math.Clamp(_settings.HistoryMaxIds, 1000, 20000);
    }

    private AppSettings ReadControls()
    {
        var settings = new AppSettings
        {
            Sorting = _sorting.SelectedIndex switch
            {
                1 => WallhavenSorting.Trending,
                2 => WallhavenSorting.Popular,
                3 => WallhavenSorting.Newest,
                _ => WallhavenSorting.Random
            },

            Category = _category.SelectedIndex switch
            {
                1 => WallhavenCategory.General,
                2 => WallhavenCategory.Anime,
                3 => WallhavenCategory.People,
                _ => WallhavenCategory.All
            },

            Query = _query.Text,

            ContentFilter = _contentFilter.SelectedIndex switch
            {
                0 => ContentFilterMode.Standard,
                2 => ContentFilterMode.Strict,
                _ => ContentFilterMode.Reduced
            },

            IntervalMinutes = (int)_interval.Value,

            FadeMilliseconds = _transition.SelectedIndex switch
            {
                0 => 0,
                1 => 500,
                2 => 750,
                3 => 1000,
                _ => 2000
            },

            ScaleMode =
                _scaleMode.SelectedIndex == 1
                    ? ImageScaleMode.Fit
                    : ImageScaleMode.Fill,

            MultiMonitorMode =
                _multiMonitor.SelectedIndex == 1
                    ? MultiMonitorMode.DifferentImage
                    : MultiMonitorMode.SameImage,

            DisplayAwareFiltering = _displayAware.Checked,
            CacheTargetFiles = (int)_cacheTargetFiles.Value,
            CacheMaxFiles = (int)_cacheMaxFiles.Value,
            CacheMaxMiB = (int)_cacheMaxMiB.Value,
            HistoryMaxIds = (int)_historyMaxIds.Value
        };

        settings.Normalize();
        return settings;
    }

    private void UpdateFilterDescription()
    {
        var mode = _contentFilter.SelectedIndex switch
        {
            0 => ContentFilterMode.Standard,
            2 => ContentFilterMode.Strict,
            _ => ContentFilterMode.Reduced
        };

        _contentFilterDescription.Text =
            ContentFilterPolicy.Description(mode);
    }

    private void ClearCache()
    {
        if (MessageBox.Show(
                this,
                "Vider uniquement les images en cache ?\n\nL’historique anti-répétition sera conservé.",
                "Wallhaven Screensaver",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var settings = ReadControls();
        var cache = new CacheStore(
            maxFiles: settings.CacheMaxFiles,
            maxBytes: (long)settings.CacheMaxMiB * 1024L * 1024L);

        cache.ClearAll();

        MessageBox.Show(
            this,
            "Cache vidé. L’historique n’a pas été modifié.",
            "Wallhaven Screensaver",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ResetHistory()
    {
        if (MessageBox.Show(
                this,
                "Réinitialiser volontairement tout l’historique des wallpapers ?\n\nCette action est indépendante du cache et ne peut pas être annulée.",
                "Wallhaven Screensaver",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var settings = ReadControls();
        new HistoryStore(settings.HistoryMaxIds).Clear();

        MessageBox.Show(
            this,
            "Historique réinitialisé.",
            "Wallhaven Screensaver",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowDiagnostics()
    {
        var settings = ReadControls();
        var history = new HistoryStore(settings.HistoryMaxIds).Snapshot();
        var cache = new CacheStore(
            maxFiles: settings.CacheMaxFiles,
            maxBytes: (long)settings.CacheMaxMiB * 1024L * 1024L).Stats();
        var counters = DiagnosticsStore.Snapshot();

        long Counter(string name) =>
            counters.TryGetValue(name, out var value) ? value : 0;

        var text =
            $"Vus aujourd’hui : {history.SeenToday.Count}\n" +
            $"Historique : {history.TotalCount}\n" +
            $"Cache : {cache.Files} fichier(s), {cache.Bytes / 1024d / 1024d:F1} MiB\n" +
            $"Téléchargements en attente : {cache.Pending}\n" +
            $"Leases d’affichage : {cache.Leased}\n\n" +
            $"Doublons jour rejetés : {Counter(DiagnosticCounters.DailyRepeat)}\n" +
            $"Historique récent rejeté : {Counter(DiagnosticCounters.RecentHistory)}\n" +
            $"Doublons cache/pending rejetés : {Counter(DiagnosticCounters.PendingDuplicate)}\n" +
            $"Rejets Strict : {Counter(DiagnosticCounters.StrictFilter)}\n" +
            $"Rejets Reduced : {Counter(DiagnosticCounters.ReducedFilter)}\n" +
            $"Affichages acceptés : {Counter(DiagnosticCounters.Accepted)}";

        MessageBox.Show(
            this,
            text,
            "Diagnostic Wallhaven Screensaver",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void SaveAndClose()
    {
        try
        {
            SettingsStore.Save(ReadControls());
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Impossible d’enregistrer les réglages.\n\n{ex.Message}",
                "Wallhaven Screensaver",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void TestScreensaver()
    {
        try
        {
            SettingsStore.Save(ReadControls());
            var executable = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executable))
                throw new InvalidOperationException("Chemin de l’exécutable introuvable.");

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "/s",
                    UseShellExecute = false
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Impossible de lancer le test.\n\n{ex.Message}",
                "Wallhaven Screensaver",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static GroupBox CreateGroup(string text, int height) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = height,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0, 0, 0, 12)
        };

    private static TableLayoutPanel CreateTwoColumnTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            AutoSize = false
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static void AddRow(
        TableLayoutPanel table,
        int row,
        string labelText,
        Control control)
    {
        while (table.RowStyles.Count <= row)
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 7, 8, 3)
        };

        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(3, 3, 3, 5);

        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static void ConfigureCombo(
        ComboBox combo,
        IEnumerable<string> items)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Items.AddRange(items.Cast<object>().ToArray());
        combo.Width = 360;
    }
}
