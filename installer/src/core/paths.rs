// Adapted from the Non-Visual Calculus installer by Rashad Naqeeb (MIT),
// https://github.com/rashadnaqeeb/NonVisualCalculus — by way of the Harkest
// Dungeon installer, https://github.com/amerikrainian/harkest-dungeon.

use std::path::{Path, PathBuf};

// The full release list, newest first: one call serves both the latest-version
// lookup and the per-version release notes shown after an update.
pub const GITHUB_RELEASES_URL: &str =
    "https://api.github.com/repos/amerikrainian/speechchem/releases?per_page=100";
pub const MOD_ZIP_PREFIX: &str = "SpeechChem-v";
pub const MOD_ZIP_SUFFIX: &str = ".zip";
pub const GAME_EXES: &[&str] = &["SpaceChem.exe"];
pub const GAME_FOLDERS: &[&str] = &["SpaceChem"];
// SpaceChem is one managed exe on Zachtronics' own engine over SDL2; the save
// database template it seeds new profiles from sits next to the exe in every
// install, so together they identify the game dir (SDL2.dll alone would match
// any SDL game).
pub const GAME_ASSEMBLY_MARKER: &str = "template.locals";
// The host dll SpaceChem.exe.config makes the CLR load: the mod's presence marker.
pub const PLUGIN_REL: &str = "SpeechChem.dll";
// Installer state lives in the mod's own folder, next to namemap.tsv and locale\.
pub const MANIFEST_REL: &str = "SpeechChem/install.json";
pub const BACKUPS_REL: &str = "SpeechChem/backups";

pub fn manifest_path(game_dir: &Path) -> PathBuf {
    game_dir.join(MANIFEST_REL)
}

pub fn normalize_rel(path: &str) -> String {
    path.replace('\\', "/").trim_start_matches("./").to_string()
}

/// Any of these present without a manifest = a hand-copied install (repair offered).
/// steam_appid.txt and Mono.Cecil.dll are deliberately not markers: too generic. Nor is
/// SpaceChem.exe.config: the stock game ships one (the mod's replaces it).
pub fn required_loader_files() -> &'static [&'static str] {
    &[
        PLUGIN_REL,
        "SpeechChem.Module.dll",
        "0Harmony.dll",
        "prism.dll",
    ]
}

// The game's startup config, the one stock file the mod zip REPLACES (it switches the
// runtime to CLR 4 and names the mod's AppDomainManager).
pub const GAME_CONFIG_REL: &str = "SpaceChem.exe.config";
// The pristine copy a developer (Debug) deploy of the mod keeps next to the config.
pub const GAME_CONFIG_VANILLA_REL: &str = "SpaceChem.exe.config.vanilla";
// Text only the mod's config contains: the AppDomainManager type it loads.
pub const MOD_CONFIG_MARKER: &str = "SpeechChem.Bootstrap";
