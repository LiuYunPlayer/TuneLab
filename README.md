# TuneLab
TuneLab is a lightweight singing voice synthesis editor that supports multiple synthesis engines and supports importing or exporting multiple project formats.
## Extension installation
Drag the `tlx` extension package file into the editor to install it.
## Extension development
You can develop your own project formats and synthesis engine extensions.
- You can place your project in the `/Extensions` folder and .gitignore will automatically ignore everything in that folder.
- Adding a `description.json` file to the extension package allows TuneLab to better support your extension. Its content is as follows:

|Field Name|Field Type|Required|Description|
|-|-|-|-|
|name|string|√|Your extension name.
|company|string|×|Your company name.
|platforms|string Array|×|Supported platforms, null or empty means all. All available values see `Platforms`
|assemblies|string Array|×|Assemblies containing extension interfaces. If null, TuneLab will try to load all assemblies.
|version|string|×|Your extension version.

- Platforms

    Platform field consists of \<OS>-\<Architecture> or \<OS> (e.g. "win-x64" "osx").
    - Available OS values: `osx` `win`
    - Available Architecture values: `x64` `x86` `arm64` `arm`

    If the value is only \<OS>, all architectures of the operating system are considered supported.

- Pack

    Compress the extension package into a zip and change the suffix to `.tlx`.

# Translation contributor
| Lang | contributor |
|------|:-----------:|
|en-US|-|
|zh-CN|-|
|zh-TW|@justln1113|
|ja-JP|@sevenc-nanashi|
|ko-KR|@Su-Yong|
|es-US|@AnotherNN|
|pt-BR|@overdramatic|
|fr-FR|@LittleAcrasy|
|nl-NL|@RhelaRazer|
|it-IT|@sykhro|
|el-GR|@A-MAIN|
|ru-RU|@Ksauxion|
|uk-UA|@Ksauxion|
|de-DE|@RedBlackAka|
|sv-SE|@ItzIcoza|
|tr-TR|@kulisfy|
