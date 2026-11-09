# 321 Back Office

Back-office de bureau natif : une vraie fenêtre (pas de web), qui affiche un Business Profile, le persiste en JSON et peut charger le business d'une autre personne.

| Brique | Choix | Raison |
| -------------- | ----------------------------------------- | --------------------------------------------------- |
| Langage | **C# 12 / .NET 8** | un seul binaire à lancer, même code Linux et Windows |
| Interface | **Avalonia 11 (XAML)** | fenêtre native et widgets standards, multiplateforme |
| Build | **SDK .NET (`dotnet build`)** | aucun outil supplémentaire après le SDK |
| Persistance | **System.Text.Json** | JSON lisible, écriture atomique (`.tmp` puis renommage) |
| Communication | **MQTT 3.1.1 (QoS 0) via `TcpClient`** | pas de dépendance externe ; s'intègre à l'infra MQTT |
| Dialogues | boîtes maison (`dotnet/src/Ui/Dialogs.cs`) | Avalonia n'en fournit pas, aucun paquet NuGet en plus |
| Scripts | `run.sh` / `run.cmd` | compile et lance en une commande |

Avalonia est récupéré par NuGet au premier build : un accès réseau est nécessaire une fois, ensuite la compilation est hors ligne.

## Prérequis

| Outil | Linux / macOS | Windows |
| -------------------- | ------------------------------------- | ---------------------------------------- |
| SDK .NET 8 | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Affichage | X11 ou Wayland | — |

Sous Linux, Avalonia a besoin des bibliothèques natives habituelles (`libx11`, `libice`, `libsm`, `libfontconfig`).

## Lancer

```bash
./run.sh                          # business par défaut (data/business.json)
./run.sh chemin/business.json     # un autre business
```

```bat
run.cmd                           :: Windows
```

Le script compile dans `dotnet/build/` puis lance l'application. `BUILD_DIR` permet de changer le dossier de sortie.

Sans les scripts :

```bash
dotnet build dotnet/BackOffice.csproj -c Release -o dotnet/build
dotnet dotnet/build/BackOffice.dll [business.json]
```

## Ligne de commande

| Commande | Effet |
| ------------------------------------ | --------------------------------------------------- |
| `BackOffice` | interface avec le business par défaut |
| `BackOffice chemin/business.json` | interface avec un autre business |
| `BackOffice --dump fichier.json` | valide et affiche le JSON normalisé (code 1 si invalide) |
| `BackOffice --publish fichier.json` | publie le business sur le broker MQTT configuré |

Avec les scripts : `./run.sh --dump data/business.json`.

## Structure

```
dotnet/BackOffice.csproj     projet .NET (C# 12, net8.0) + paquets Avalonia
dotnet/Program.cs            point d'entrée + modes --dump / --publish
dotnet/App.axaml(.cs)        application Avalonia (thème sombre)
dotnet/src/Model/            BusinessProfile, Product, Validation
dotnet/src/Io/               BusinessStore (JSON), ImageLoader (chemins + base64)
dotnet/src/Mqtt/             MqttPublisher (MQTT 3.1.1 QoS 0)
dotnet/src/Cli/              CliCommands (--dump / --publish)
dotnet/src/Ui/               MainWindow (XAML + code), Dialogues, lignes de liste
data/                        business.json (COM-0112) + exemple-business-externe.json
assets/                      logos 16:9 et images produits carrées
run.sh / run.cmd             build + lancement
```

## Modèle de données

### Business Profile (BP)

| Propriété | Type | Contrainte |
| --------- | ---------- | -------------------------------------------------------------- |
| `ID` | string | **obligatoire**, `^COM-[0-9]{4}$`, identifie le commerce sans ambiguïté |
| `Name` | string | **obligatoire**, nom public du commerce |
| `Logo` | image 16:9 | **obligatoire**, chemin relatif au JSON ou data URI base64 |
| `Time` | heure | `HH:mm` ; si absente (ou invalide), l'heure du PC est utilisée |
| `Special` | produit | **obligatoire**, produit phare (objet complet), doit faire partie des produits |

### Produit

| Propriété | Type | Contrainte |
| --------- | ---------- | -------------------------------------------------------------- |
| `ID` | string | **obligatoire**, `^PROD-[0-9]{4}$`, unique dans le business |
| `Name` | string | **obligatoire**, nom d'affichage |
| `Picture` | image carrée | chemin relatif au JSON ou data URI base64 |
| `Price` | nombre | prix unitaire **en francs** (24.50 CHF → `24.5`) ; stocké en centimes en interne |

Le prix est stocké en centimes dans le code (pour éviter les erreurs d'arrondi) et sérialisé en francs ; l'interface l'affiche en `24.50 CHF`.

### Format JSON

```json
{
    "ID": "COM-7777",
    "Name": "Aux Mille Miettes",
    "Logo": "data:image/webp;base64,UklGR...",
    "Time": "09:30",
    "Special": {
        "ID": "PROD-7777",
        "Name": "Millefeuille",
        "Picture": "data:image/jpeg;base64,/9j/4AAQ...",
        "Price": 3.5
    }
}
```

- `Logo` et `Picture` acceptent un chemin relatif au JSON ou un data URI base64 ; l'enregistrement et l'export embarquent toujours les images en data URI base64.
- `Special` est le **produit phare complet** (objet), pas seulement son ID.
- `Products` n'est écrit **que s'il existe d'autres produits** que le produit phare, pour ne jamais perdre la liste du back-office.
- `Time` est l'heure du commerce, au format `HH:mm`. Si elle est absente (ou invalide), l'heure du PC est utilisée et écrite à la sauvegarde ; le bouton « Heure du PC » la remplit à tout moment.
- La lecture reste tolérante : clés `ID`/`id`, `Special` objet ou simple ID, `Price` en francs ou `priceCents` en centimes.

Les chemins d'images sont **relatifs au fichier JSON** : un dossier `data/` + `assets/` reste donc portable d'une machine à l'autre. Si un chemin n'existe pas à côté du JSON, la version C# cherche le même chemin dans le dossier parent (ce qui fait fonctionner tel quel `data/business.json` pointant sur `assets/` à la racine du dépôt).

## Fonctionnalités

- **Afficher son business** : logo 16:9, nom, ID, produit phare (image carrée, nom, prix) et liste des produits.
- **Éditer** : nom, ID, logo, produit phare, ajout / modification / suppression de produits.
- **Enregistrer** (`Ctrl+S`) dans `data/business.json`, puis **relire au démarrage**.
- **Afficher un autre BP** : bouton *Ouvrir un business…* ou argument en ligne de commande. Un exemple est fourni : `data/exemple-business-externe.json` (COM-0042, « Le Mille-Miettes »).
- **Exporter en un fichier** : les images sont intégrées en base64, ce qui donne **un seul `.json`** à envoyer par Discord.
- **Publier en MQTT** : bouton *Publier (MQTT)* ou mode CLI.
- Validation systématique : ID hors format, ID produit en double, produit phare inexistant, prix négatif, images manquantes (avertissement, pas blocage).

Le business livré par défaut est `data/business.json` : **COM-0112 « DJ Dam's »**, produit phare **PROD-1109 « eclair au chocolat »**.

## Persistance

- Fichier par défaut : `data/business.json`, retrouvé en remontant depuis le binaire jusqu'à la racine du dépôt.
- Surcharge : variable `BACKOFFICE_DATA_FILE=/chemin/vers/business.json`.
- Écriture atomique (fichier temporaire puis renommage) : en cas d'arrêt brutal, l'ancien fichier reste intact.

## Échanger un business (Discord)

1. Dans le back-office : *Exporter en un fichier…* → `business-COM-0042.json` (images incluses).
2. Envoi du fichier sur Discord.
3. Chez le destinataire : *Ouvrir un business…*, ou `./run.sh chemin/business-COM-0042.json`.

Le chargement accepte aussi bien les chemins relatifs (dossier `data/` + `assets/`) que les data URI base64.

## MQTT

Le back-office publie le business sérialisé (JSON compact) sur un topic, en MQTT 3.1.1 / QoS 0. Aucun module externe n'est requis : le client est implémenté dans `dotnet/src/Mqtt/`.

| Variable | Exemple | Rôle |
| ------------------------ | ------------------------------- | -------------------------- |
| `BACKOFFICE_MQTT_URL` | `mqtt://192.168.1.10:1883` | broker |
| `BACKOFFICE_MQTT_HOST` / `BACKOFFICE_MQTT_PORT` | `localhost` / `1883` | alternative à l'URL |
| `BACKOFFICE_MQTT_TOPIC` | `321/business/COM-0112` | **requis** pour activer MQTT |
| `BACKOFFICE_MQTT_CLIENT_ID` | `backoffice-dj-dams` | optionnel |

Test en ligne de commande :

```bash
BACKOFFICE_MQTT_URL=mqtt://localhost:1883 BACKOFFICE_MQTT_TOPIC=321/business \
  ./run.sh --publish data/business.json
```

Le bouton *Publier (MQTT)* est désactivé tant que `BACKOFFICE_MQTT_TOPIC` n'est pas défini.

## Référence commune des modèles (TODO d'équipe)

Le modèle de référence vit dans `dotnet/src/Model/`. Reste à décider **où** cette référence commune doit être publiée : dépôt Git partagé, fichier `schema/business.schema.json`, ou package NuGet. En attendant, les échanges utilisent le JSON `ID` / `Name` / `Logo` / `Special` décrit plus haut.
