package dev.dotori.jetbrains;

import com.intellij.codeInsight.actions.onSave.FormatOnSaveOptionsBase;
import com.intellij.openapi.fileTypes.FileType;
import org.jetbrains.annotations.NotNull;

import java.util.Collection;
import java.util.List;

public final class DotoriFormatOnSaveDefaultsProvider implements FormatOnSaveOptionsBase.DefaultsProvider {
    @Override
    public @NotNull Collection<@NotNull FileType> getFileTypesFormattedOnSaveByDefault() {
        return List.of(DotoriFileType.INSTANCE);
    }
}
