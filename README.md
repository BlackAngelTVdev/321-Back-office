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
./run.sh                          # exemple par défaut (data/exemple-business-externe.json)
./run.sh chemin/business.json     # un autre business
./run.sh --no-build               # relance l'interface sans recompiler
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
| `BackOffice` | interface avec l'exemple par défaut (`data/exemple-business-externe.json`) |
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
dotnet/src/Io/               BusinessStore (JSON), ImageLoader (chemins + base64), EnvFile (.env)
dotnet/src/Mqtt/             MqttPublisher (MQTT 3.1.1 QoS 0)
dotnet/src/Cli/              CliCommands (--dump / --publish)
dotnet/src/Ui/               MainWindow (XAML + code), Dialogues, lignes de liste
data/                        business.json (COM-0112) + exemple-business-externe.json + time.json (heure)
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
- L'heure ne fait **pas** partie du business : elle est lue dans `data/time.json` (voir ci-dessous) et n'est jamais écrite dans le fichier du commerce.
- La lecture reste tolérante : clés `ID`/`id`, `Special` objet ou simple ID, `Price` en francs ou `priceCents` en centimes.

### Heure (`data/time.json`)

L'heure affichée ne vient **pas** du JSON du business : elle est lue dans un fichier séparé, `data/time.json` (ou le chemin de `BACKOFFICE_TIME_FILE`), au format :

```json
{ "UnixTimeStamp": 1794399763 }
```

- L'interface l'affiche en heure locale `HH:mm` dans la barre du haut, avec le timestamp brut, et **relit le fichier chaque minute**.
- Le back-office ne modifie jamais ce fichier : c'est la source d'heure, pas une donnée du commerce.
- S'il est absent ou invalide, l'heure du PC est affichée (un avertissement le signale au chargement).

Si `BACKOFFICE_MQTT_TIME_TOPIC` est configuré, l'application s'abonne à ce topic : **chaque message reçu met immédiatement l'heure à jour** et devient prioritaire sur `time.json`. L'heure continue ensuite d'avancer à partir du dernier timestamp reçu. La barre affiche alors `Heure : HH:mm (MQTT)`.
- Un `time.json` placé à côté du business ouvert est prioritaire sur celui de `data/`.

Les chemins d'images sont **relatifs au fichier JSON** : un dossier `data/` + `assets/` reste donc portable d'une machine à l'autre. Si un chemin n'existe pas à côté du JSON, la version C# cherche le même chemin dans le dossier parent (ce qui fait fonctionner tel quel `data/business.json` pointant sur `assets/` à la racine du dépôt).

## Fonctionnalités

- **Afficher son business** : logo 16:9, nom, ID, produit phare (image carrée, nom, prix), liste des produits et l'heure lue dans `data/time.json`.
- **Éditer** : nom, ID, logo, produit phare, ajout / modification / suppression de produits.
- **Enregistrer** (`Ctrl+S`) dans le fichier ouvert, puis **relire au démarrage** (l'exemple `data/exemple-business-externe.json` est chargé par défaut).
- **Afficher un autre BP** : bouton *Ouvrir un business…* ou argument en ligne de commande. Un exemple est fourni : `data/exemple-business-externe.json` (COM-0042, « Le Mille-Miettes »).
- **Exporter en un fichier** : les images sont intégrées en base64, ce qui donne **un seul `.json`** à envoyer par Discord.
- **Publier en MQTT** : bouton *Publier (MQTT)* ou mode CLI.
- Validation systématique : ID hors format, ID produit en double, produit phare inexistant, prix négatif, images manquantes (avertissement, pas blocage).

Au démarrage, l'interface charge `data/exemple-business-externe.json` : **COM-0042 « Le Mille-Miettes »**, produit phare **PROD-0104 « Tarte aux myrtilles »**, l'heure venant de `data/time.json`. Le bouton *Mes données* ouvre `data/business.json` : **COM-0112 « DJ Dam's »**, produit phare **PROD-1109 « eclair au chocolat »**.

## Persistance

- Fichier affiché au démarrage : `data/exemple-business-externe.json`, retrouvé en remontant depuis le binaire jusqu'à la racine du dépôt.
- Bouton *Mes données* : `data/business.json`.
- Surcharge : variable `BACKOFFICE_DATA_FILE=/chemin/vers/business.json` (utilisée au démarrage et par *Mes données*).
- Heure : `data/time.json`, relu chaque minute et jamais réécrit ; surcharge `BACKOFFICE_TIME_FILE=/chemin/vers/time.json`.
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
| `BACKOFFICE_MQTT_TOPIC` | `umddcmid3b/business` | topic de publication (**requis** pour le bouton) |
| `BACKOFFICE_MQTT_USERNAME` / `BACKOFFICE_MQTT_PASSWORD` | `ict` / `…` | identifiants du broker |
| `BACKOFFICE_MQTT_TIME_TOPIC` | `umddcmid3b/dg/time` | topic d'heure, abonnement automatique |
| `BACKOFFICE_MQTT_CLIENT_ID` | `backoffice-dj-dams` | optionnel |

### Fichier `.env`

Au démarrage, l'application charge le premier `.env` trouvé (`BACKOFFICE_ENV_FILE`, sinon en remontant depuis le binaire ou le dossier courant). Les variables déjà présentes dans l'environnement gardent la priorité. `.env` est ignoré par Git (mot de passe) ; `.env.example` sert de modèle.

```ini
BACKOFFICE_MQTT_HOST=mqqt.blue.section-inf.ch
BACKOFFICE_MQTT_PORT=1883
BACKOFFICE_MQTT_USERNAME=ict
BACKOFFICE_MQTT_PASSWORD=…
BACKOFFICE_MQTT_TIME_TOPIC=umddcmid3b/dg/time
BACKOFFICE_MQTT_TOPIC=umddcmid3b/business
```

Abonnement à l'heure, sans interface :

```bash
mosquitto_sub -h mqqt.blue.section-inf.ch -u ict -P … -t umddcmid3b/dg/time -v
```

Test en ligne de commande :

```bash
BACKOFFICE_MQTT_URL=mqtt://localhost:1883 BACKOFFICE_MQTT_TOPIC=321/business \
  ./run.sh --publish data/business.json
```

Le bouton *Publier (MQTT)* est désactivé tant que `BACKOFFICE_MQTT_TOPIC` n'est pas défini.

## Référence commune des modèles (TODO d'équipe)

Le modèle de référence vit dans `dotnet/src/Model/`. Reste à décider **où** cette référence commune doit être publiée : dépôt Git partagé, fichier `schema/business.schema.json`, ou package NuGet. En attendant, les échanges utilisent le JSON `ID` / `Name` / `Logo` / `Special` décrit plus haut.
