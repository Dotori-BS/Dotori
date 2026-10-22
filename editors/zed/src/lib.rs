use zed_extension_api as zed;

struct DotoriExtension;

impl zed::Extension for DotoriExtension {
    fn new() -> Self {
        Self
    }

    fn language_server_command(
        &mut self,
        language_server_id: &zed::LanguageServerId,
        worktree: &zed::Worktree,
    ) -> zed::Result<zed::Command> {
        let binary =
            zed::settings::LspSettings::for_worktree(language_server_id.as_ref(), worktree)?.binary;
        let command = binary
            .as_ref()
            .and_then(|b| b.path.clone())
            .or_else(|| worktree.which("dotori"))
            .ok_or_else(|| {
                "dotori was not found on PATH; set lsp.dotori-lsp.binary.path in Zed settings"
                    .to_string()
            })?;
        let args = binary
            .as_ref()
            .and_then(|b| b.arguments.clone())
            .unwrap_or_else(|| vec!["lsp".to_string()]);
        let mut env = worktree.shell_env();
        if let Some(overrides) = binary.and_then(|b| b.env) {
            env.retain(|(key, _)| !overrides.contains_key(key));
            env.extend(overrides);
        }

        Ok(zed::Command { command, args, env })
    }
}

zed::register_extension!(DotoriExtension);
