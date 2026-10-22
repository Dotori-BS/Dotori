package dev.dotori.jetbrains;

import com.intellij.openapi.vfs.VirtualFile;
import com.intellij.platform.lsp.api.customization.LspCustomization;
import com.intellij.platform.lsp.api.customization.LspFormattingCustomizer;
import com.intellij.platform.lsp.api.customization.LspFormattingSupport;
import org.jetbrains.annotations.NotNull;

public final class DotoriLspCustomization extends LspCustomization {
    private final LspFormattingSupport formatting = new LspFormattingSupport() {
        @Override
        public boolean shouldFormatThisFileExclusivelyByServer(
                @NotNull VirtualFile file, boolean ideCanFormatThisFileItself,
                boolean serverExplicitlyWantsToFormatThisFile) {
            // Dotori advertises static formatting capability, not dynamic registration.
            return file.getFileType() == DotoriFileType.INSTANCE;
        }
    };

    @Override
    public @NotNull LspFormattingCustomizer getFormattingCustomizer() {
        return formatting;
    }
}
