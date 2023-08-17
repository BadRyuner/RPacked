# RPacked
🚀 Memory safe, blazing fast packer for .Net 8.0 🚀

Packs any >= .Net 8.0 (below is not supported due to hostfxr limitations) application into a single exe. 
The brotli algorithm is used for data compression.

Using RPacker as an example:
Packer weight with dependencies: 57 + 502 + 315 + 39 + 12 = 925 kb
Packed packer weight: 717 kb
All numbers without the extra files (bootstrapper.exe and runtimeconfig.json);

The bootstrapper itself weighs 432kb in Releases mode. In comparison, the regular appnethost weighs 154kb, but it loads the dll from a file.

# Usage
`RPacker.exe path/to/your/app.dll`

# TODOs
- [ ] AntiDump protection
- [ ] Reduce the weight of bootstrapper.exe
