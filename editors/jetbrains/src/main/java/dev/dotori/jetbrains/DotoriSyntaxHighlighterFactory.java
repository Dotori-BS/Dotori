package dev.dotori.jetbrains;

import com.intellij.lexer.Lexer;
import com.intellij.openapi.editor.DefaultLanguageHighlighterColors;
import com.intellij.openapi.editor.HighlighterColors;
import com.intellij.openapi.editor.colors.TextAttributesKey;
import com.intellij.openapi.fileTypes.SyntaxHighlighter;
import com.intellij.openapi.fileTypes.SyntaxHighlighterBase;
import com.intellij.openapi.fileTypes.SyntaxHighlighterFactory;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.vfs.VirtualFile;
import com.intellij.psi.TokenType;
import com.intellij.psi.tree.IElementType;
import org.jetbrains.annotations.NotNull;
import org.jetbrains.annotations.Nullable;

public final class DotoriSyntaxHighlighterFactory extends SyntaxHighlighterFactory {
    @Override
    public @NotNull SyntaxHighlighter getSyntaxHighlighter(@Nullable Project project, @Nullable VirtualFile file) {
        return new SyntaxHighlighterBase() {
            @Override public @NotNull Lexer getHighlightingLexer() { return new DotoriLexer(); }

            @Override
            public TextAttributesKey @NotNull [] getTokenHighlights(IElementType type) {
                if (type == DotoriLexer.COMMENT) return pack(DefaultLanguageHighlighterColors.LINE_COMMENT);
                if (type == DotoriLexer.STRING) return pack(DefaultLanguageHighlighterColors.STRING);
                if (type == DotoriLexer.NUMBER) return pack(DefaultLanguageHighlighterColors.NUMBER);
                if (type == DotoriLexer.KEYWORD) return pack(DefaultLanguageHighlighterColors.KEYWORD);
                if (type == DotoriLexer.IDENTIFIER) return pack(DefaultLanguageHighlighterColors.IDENTIFIER);
                if (type == DotoriLexer.BRACKET) return pack(DefaultLanguageHighlighterColors.BRACES);
                if (type == DotoriLexer.OPERATOR) return pack(DefaultLanguageHighlighterColors.OPERATION_SIGN);
                if (type == TokenType.BAD_CHARACTER) return pack(HighlighterColors.BAD_CHARACTER);
                return TextAttributesKey.EMPTY_ARRAY;
            }
        };
    }
}
