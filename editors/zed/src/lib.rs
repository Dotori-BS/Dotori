use zed_extension_api as zed;

struct DotoriExtension;

impl zed::Extension for DotoriExtension {
    fn new() -> Self {
        Self
    }

    fn language_server_command(
        &mut self,
        _language_server_id: &zed::LanguageServerId,
        worktree: &zed::Worktree,
    ) -> zed::Result<zed::Command> {
        let command = worktree.which("dotori").ok_or_else(|| {
            "dotori was not found on PATH; install Dotori or add it to your shell PATH".to_string()
        })?;

        Ok(zed::Command {
            command,
            args: vec!["lsp".to_string()],
            env: worktree.shell_env(),
        })
    }
}

zed::register_extension!(DotoriExtension);
