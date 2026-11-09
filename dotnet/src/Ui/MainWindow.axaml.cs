using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BackOffice.Io;
using BackOffice.Model;
using BackOffice.Mqtt;

namespace BackOffice.Ui;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _statusTimer = new();
    private readonly List<ProductRow> _productRows = new();

    private BusinessProfile _business = new();
    private string _filePath = string.Empty;
    private string _baseDir = Directory.GetCurrentDirectory();
    private List<string> _warnings = new();
    private Bitmap? _logoBitmap;
    private Bitmap? _specialBitmap;
    private bool _suppressSpecialChange;
    private string? _pendingFile;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(string? initialFile)
    {
        _pendingFile = initialFile;

        InitializeComponent();
        WireEvents();

        _statusTimer.Tick += OnStatusTimerTick;

        PublishButton.IsEnabled = MqttPublisher.ConfigFromEnvironment() is not null;
        ToolTip.SetTip(
            PublishButton,
            "Publie le business sur le broker défini par BACKOFFICE_MQTT_URL et BACKOFFICE_MQTT_TOPIC.");

        RefreshHeader();
        RefreshProducts();
        RefreshSpecialCombo();
    }

    /// <summary>Fichier de données par défaut (surchargé par BACKOFFICE_DATA_FILE).</summary>
    public static string DefaultDataFile()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("BACKOFFICE_DATA_FILE");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var found = FindDataFile(start);
            if (found is not null)
            {
                return found;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "data", "business.json");
    }

    private static string? FindDataFile(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "data", "business.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var file = _pendingFile;
        _pendingFile = null;

        if (!string.IsNullOrWhiteSpace(file))
        {
            await OpenBusinessAsync(file);
        }
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await SaveCurrentAsync();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _logoBitmap?.Dispose();
        _specialBitmap?.Dispose();
        DisposeRows();
        base.OnClosed(e);
    }

    private void WireEvents()
    {
        OpenButton.Click += async (_, _) => await OpenFileDialogAsync();
        MineButton.Click += async (_, _) => await OpenBusinessAsync(DefaultDataFile());
        SaveButton.Click += async (_, _) => await SaveCurrentAsync();
        ExportButton.Click += async (_, _) => await ExportSingleFileAsync();
        PublishButton.Click += async (_, _) => await PublishCurrentAsync();

        BrowseLogoButton.Click += async (_, _) => await BrowseLogoAsync();
        BrowsePictureButton.Click += async (_, _) => await BrowsePictureAsync();

        NameBox.LostFocus += (_, _) => ApplyNameEdit();
        IdBox.LostFocus += (_, _) => ApplyIdEdit();
        LogoBox.LostFocus += (_, _) => ApplyLogoEdit();
        SpecialCombo.SelectionChanged += (_, _) => ApplySpecial();

        ProductList.SelectionChanged += (_, _) => SelectProduct(ProductList.SelectedIndex);

        AddButton.Click += async (_, _) => await AddProductAsync();
        UpdateButton.Click += async (_, _) => await UpdateProductAsync();
        RemoveButton.Click += async (_, _) => await RemoveProductAsync();
    }

    // ---------- Affichage ----------

    private void ApplyBusinessToUi()
    {
        IdBox.Text = _business.Id;
        NameBox.Text = _business.Name;
        LogoBox.Text = _business.Logo;

        RefreshSpecialCombo();
        RefreshProducts();
        RefreshHeader();
        UpdateWindowTitle();
    }

    private void RefreshHeader()
    {
        SetBitmap(LogoImage, _business.Logo, 480, 270, ref _logoBitmap);

        NameLabel.Text = string.IsNullOrWhiteSpace(_business.Name) ? "(sans nom)" : _business.Name;
        IdLabel.Text = string.IsNullOrWhiteSpace(_business.Id) ? "(sans ID)" : _business.Id;

        var special = _business.FindProduct(_business.SpecialProductId);
        if (special is not null)
        {
            SetBitmap(SpecialImage, special.Picture, 88, 88, ref _specialBitmap);
            SpecialName.Text = $"{special.Name}\n{special.Id}";
            SpecialPrice.Text = Validation.FormatPrice(special.PriceCents);
        }
        else
        {
            SetBitmap(SpecialImage, null, 88, 88, ref _specialBitmap);
            SpecialName.Text = "Aucun produit phare";
            SpecialPrice.Text = "—";
        }
    }

    private void SetBitmap(Image target, string? reference, int width, int height, ref Bitmap? slot)
    {
        var next = ImageLoader.Load(reference, _baseDir) ?? ImageLoader.Placeholder(width, height);
        target.Source = next;
        slot?.Dispose();
        slot = next;
    }

    private void RefreshProducts(int selectRow = -1)
    {
        ProductList.ItemsSource = null;
        DisposeRows();

        foreach (var product in _business.Products)
        {
            var icon = ImageLoader.Load(product.Picture, _baseDir) ?? ImageLoader.Placeholder(48, 48);
            _productRows.Add(new ProductRow(
                icon,
                $"{product.Id} — {product.Name} — {Validation.FormatPrice(product.PriceCents)}"));
        }

        ProductList.ItemsSource = _productRows;

        if (selectRow >= 0 && selectRow < _productRows.Count)
        {
            ProductList.SelectedIndex = selectRow;
        }
    }

    private void DisposeRows()
    {
        foreach (var row in _productRows)
        {
            row.Icon?.Dispose();
        }

        _productRows.Clear();
    }

    private void RefreshSpecialCombo()
    {
        _suppressSpecialChange = true;
        try
        {
            var labels = _business.Products
                .Select(product => $"{product.Id} — {product.Name}")
                .ToList();

            SpecialCombo.ItemsSource = labels;
            SpecialCombo.SelectedIndex = _business.IndexOfProduct(_business.SpecialProductId);
        }
        finally
        {
            _suppressSpecialChange = false;
        }
    }

    // ---------- Édition de la fiche ----------

    private void ApplyNameEdit()
    {
        var name = (NameBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            NameBox.Text = _business.Name;
            _ = Dialogs.ShowWarningAsync(this, "Nom manquant", "Le nom du commerce ne peut pas être vide.");
            return;
        }

        _business.Name = name;
        RefreshHeader();
        UpdateWindowTitle();
    }

    private void ApplyIdEdit()
    {
        var id = (IdBox.Text ?? string.Empty).Trim();
        if (id == _business.Id)
        {
            return;
        }

        if (!Validation.IsValidBusinessId(id))
        {
            IdBox.Text = _business.Id;
            _ = Dialogs.ShowWarningAsync(
                this,
                "ID invalide",
                "L'ID doit respecter le format COM-XXXX (COM- suivi de 4 chiffres).");
            return;
        }

        _business.Id = id;
        RefreshHeader();
        UpdateWindowTitle();
    }

    private void ApplyLogoEdit()
    {
        _business.Logo = (LogoBox.Text ?? string.Empty).Trim();
        RefreshHeader();
    }

    private void ApplySpecial()
    {
        if (_suppressSpecialChange)
        {
            return;
        }

        var index = SpecialCombo.SelectedIndex;
        if (index < 0 || index >= _business.Products.Count)
        {
            return;
        }

        _business.SpecialProductId = _business.Products[index].Id;
        RefreshHeader();
    }

    private void SelectProduct(int index)
    {
        if (index < 0 || index >= _business.Products.Count)
        {
            return;
        }

        var product = _business.Products[index];
        ProductIdBox.Text = product.Id;
        ProductNameBox.Text = product.Name;
        ProductPictureBox.Text = product.Picture;
        PriceBox.Value = product.PriceCents / 100m;
    }

    // ---------- Édition des produits ----------

    private Product ReadProductForm() => new()
    {
        Id = (ProductIdBox.Text ?? string.Empty).Trim(),
        Name = (ProductNameBox.Text ?? string.Empty).Trim(),
        Picture = (ProductPictureBox.Text ?? string.Empty).Trim(),
        PriceCents = ReadPriceCents(),
    };

    private int ReadPriceCents()
        => (int)Math.Round((PriceBox.Value ?? 0m) * 100m, MidpointRounding.AwayFromZero);

    private async Task AddProductAsync()
    {
        var product = ReadProductForm();

        if (!Validation.IsValidProductId(product.Id))
        {
            await Dialogs.ShowWarningAsync(this, "ID invalide", "L'ID produit doit respecter le format PROD-XXXX.");
            return;
        }

        if (product.Name.Length == 0)
        {
            await Dialogs.ShowWarningAsync(this, "Nom manquant", "Le nom du produit ne peut pas être vide.");
            return;
        }

        if (_business.IndexOfProduct(product.Id) >= 0)
        {
            await Dialogs.ShowWarningAsync(this, "ID en double", $"Un produit utilise déjà l'ID {product.Id}.");
            return;
        }

        _business.Products.Add(product);
        if (_business.SpecialProductId.Length == 0)
        {
            _business.SpecialProductId = product.Id;
        }

        RefreshSpecialCombo();
        RefreshProducts(_business.Products.Count - 1);
        RefreshHeader();
        ShowStatus($"Produit {product.Id} ajouté.");
    }

    private async Task UpdateProductAsync()
    {
        var row = ProductList.SelectedIndex;
        if (row < 0 || row >= _business.Products.Count)
        {
            return;
        }

        var updated = ReadProductForm();

        if (!Validation.IsValidProductId(updated.Id))
        {
            await Dialogs.ShowWarningAsync(this, "ID invalide", "L'ID produit doit respecter le format PROD-XXXX.");
            return;
        }

        if (updated.Name.Length == 0)
        {
            await Dialogs.ShowWarningAsync(this, "Nom manquant", "Le nom du produit ne peut pas être vide.");
            return;
        }

        var existing = _business.IndexOfProduct(updated.Id);
        if (existing >= 0 && existing != row)
        {
            await Dialogs.ShowWarningAsync(
                this, "ID en double", $"Un autre produit utilise déjà l'ID {updated.Id}.");
            return;
        }

        var previousId = _business.Products[row].Id;
        _business.Products[row] = updated;

        if (_business.SpecialProductId == previousId)
        {
            _business.SpecialProductId = updated.Id;
        }

        RefreshSpecialCombo();
        RefreshProducts(row);
        RefreshHeader();
        ShowStatus($"Produit {updated.Id} mis à jour.");
    }

    private async Task RemoveProductAsync()
    {
        var row = ProductList.SelectedIndex;
        if (row < 0 || row >= _business.Products.Count)
        {
            return;
        }

        var removedId = _business.Products[row].Id;
        if (!await Dialogs.ConfirmAsync(this, "Supprimer le produit", $"Supprimer {removedId} ?"))
        {
            return;
        }

        _business.Products.RemoveAt(row);
        if (_business.SpecialProductId == removedId)
        {
            _business.SpecialProductId = _business.Products.Count == 0 ? string.Empty : _business.Products[0].Id;
        }

        RefreshSpecialCombo();
        RefreshProducts(Math.Min(row, _business.Products.Count - 1));
        RefreshHeader();
    }

    // ---------- Fichiers ----------

    private async Task OpenFileDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Ouvrir un business (JSON)",
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(_baseDir),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Business") { Patterns = new[] { "*.json" } },
                new FilePickerFileType("Tous les fichiers") { Patterns = new[] { "*" } },
            },
        });

        if (files.Count > 0)
        {
            await OpenBusinessAsync(files[0].Path.LocalPath);
        }
    }

    public async Task<bool> OpenBusinessAsync(string filePath)
    {
        var result = BusinessStore.Load(filePath);
        if (!result.Ok)
        {
            await Dialogs.ShowErrorAsync(this, "Fichier invalide", string.Join('\n', result.Errors));
            ShowStatus($"Échec du chargement : {filePath}", 6000);
            return false;
        }

        _business = result.Business;
        _filePath = filePath;
        _baseDir = BusinessStore.DirectoryOf(filePath);
        _warnings = result.Warnings;

        ApplyBusinessToUi();

        ShowStatus(
            _warnings.Count == 0
                ? $"Chargé : {filePath}"
                : $"Chargé : {filePath} — {string.Join(" · ", _warnings)}",
            _warnings.Count == 0 ? 6000 : 10000);

        return true;
    }

    private async Task SaveCurrentAsync()
    {
        if (_filePath.Length == 0)
        {
            await ExportSingleFileAsync();
            return;
        }

        await SaveToAsync(_filePath, embedImages: false);
    }

    private async Task ExportSingleFileAsync()
    {
        var suggested = _business.Id.Length == 0
            ? "business-export.json"
            : $"business-{_business.Id}.json";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter le business (images incluses)",
            SuggestedFileName = suggested,
            DefaultExtension = "json",
            SuggestedStartLocation = await TryGetFolderAsync(_baseDir),
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });

        if (file is null)
        {
            return;
        }

        await SaveToAsync(file.Path.LocalPath, embedImages: true);
    }

    private async Task<bool> SaveToAsync(string path, bool embedImages)
    {
        var problems = ValidateBusiness();
        if (problems.Count > 0)
        {
            await Dialogs.ShowWarningAsync(this, "Données incomplètes", string.Join('\n', problems));
            return false;
        }

        if (!BusinessStore.Save(path, _business, embedImages, out var error))
        {
            await Dialogs.ShowErrorAsync(this, "Échec de l'enregistrement", error ?? "Erreur inconnue.");
            return false;
        }

        _filePath = path;
        _baseDir = BusinessStore.DirectoryOf(path);
        UpdateWindowTitle();
        ShowStatus($"Enregistré : {path}", 6000);
        return true;
    }

    private async Task PublishCurrentAsync()
    {
        var config = MqttPublisher.ConfigFromEnvironment();
        if (config is null)
        {
            await Dialogs.ShowWarningAsync(
                this,
                "MQTT non configuré",
                "Définissez BACKOFFICE_MQTT_URL (ex. mqtt://localhost:1883) et BACKOFFICE_MQTT_TOPIC.");
            return;
        }

        var payload = BusinessStore.Serialize(_business, _baseDir, embedImages: false, indented: false);

        if (!MqttPublisher.Publish(config, payload, out var error))
        {
            await Dialogs.ShowErrorAsync(this, "Échec de la publication MQTT", error ?? "Erreur inconnue.");
            return;
        }

        ShowStatus($"Publié sur {config.Topic}", 6000);
    }

    // ---------- Assistants ----------

    private async Task BrowseLogoAsync()
    {
        var file = await PickImageAsync("Choisir un logo 16:9");
        if (file is null)
        {
            return;
        }

        var reference = RelativeImagePath(file);
        LogoBox.Text = reference;
        _business.Logo = reference;
        RefreshHeader();
    }

    private async Task BrowsePictureAsync()
    {
        var file = await PickImageAsync("Choisir une image produit");
        if (file is not null)
        {
            ProductPictureBox.Text = RelativeImagePath(file);
        }
    }

    private async Task<string?> PickImageAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(_baseDir),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" },
                },
            },
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<IStorageFolder?> TryGetFolderAsync(string path)
    {
        try
        {
            var uri = new Uri(Path.GetFullPath(path) + Path.DirectorySeparatorChar);
            return await StorageProvider.TryGetFolderFromPathAsync(uri);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private List<string> ValidateBusiness()
    {
        var problems = new List<string>();

        if (!Validation.IsValidBusinessId(_business.Id))
        {
            problems.Add("ID du commerce invalide (format attendu COM-XXXX).");
        }

        if (string.IsNullOrWhiteSpace(_business.Name))
        {
            problems.Add("Le nom du commerce est vide.");
        }

        if (_business.Products.Count == 0)
        {
            problems.Add("Le business doit contenir au moins un produit.");
        }

        foreach (var product in _business.Products)
        {
            if (!Validation.IsValidProductId(product.Id))
            {
                problems.Add($"Produit {product.Name} : ID invalide (format attendu PROD-XXXX).");
            }

            if (string.IsNullOrWhiteSpace(product.Name))
            {
                problems.Add($"Produit {product.Id} : nom vide.");
            }

            if (product.PriceCents < 0)
            {
                problems.Add($"Produit {product.Id} : prix négatif.");
            }
        }

        if (_business.IndexOfProduct(_business.SpecialProductId) < 0)
        {
            problems.Add("Le produit phare doit référencer un produit existant.");
        }

        return problems;
    }

    private string RelativeImagePath(string absolutePath)
    {
        var relative = Path.GetRelativePath(_baseDir, absolutePath);
        return relative.StartsWith("..", StringComparison.Ordinal)
            ? absolutePath
            : relative.Replace('\\', '/');
    }

    private void UpdateWindowTitle()
    {
        var suffix = _business.Name.Length == 0
            ? string.Empty
            : $" — {_business.Name} ({_business.Id})";

        Title = "321 Back Office" + suffix;
    }

    private void ShowStatus(string message, int milliseconds = 4000)
    {
        StatusLabel.Text = message;
        _statusTimer.Stop();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(milliseconds, 500));
        _statusTimer.Start();
    }

    private void OnStatusTimerTick(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        StatusLabel.Text = string.Empty;
    }
}
