package dev.dotori.jetbrains;

import com.intellij.lang.Language;

public final class DotoriLanguage extends Language {
    public static final DotoriLanguage INSTANCE = new DotoriLanguage();

    private DotoriLanguage() {
        super("Dotori");
    }
}
