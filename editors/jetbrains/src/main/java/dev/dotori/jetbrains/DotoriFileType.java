package dev.dotori.jetbrains;

import com.intellij.openapi.fileTypes.LanguageFileType;
import org.jetbrains.annotations.NotNull;
import org.jetbrains.annotations.Nullable;

import javax.swing.Icon;

public final class DotoriFileType extends LanguageFileType {
    public static final DotoriFileType INSTANCE = new DotoriFileType();

    private DotoriFileType() {
        super(DotoriLanguage.INSTANCE);
    }

    @Override
    public @NotNull String getName() {
        return "Dotori";
    }

    @Override
    public @NotNull String getDescription() {
        return "Dotori C++ build project";
    }

    @Override
    public @NotNull String getDefaultExtension() {
        return "dotori";
    }

    @Override
    public @Nullable Icon getIcon() {
        return null;
    }
}
