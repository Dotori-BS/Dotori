package dev.dotori.jetbrains;

import com.intellij.lexer.LexerBase;
import com.intellij.psi.TokenType;
import com.intellij.psi.tree.IElementType;
import org.jetbrains.annotations.NotNull;
import org.jetbrains.annotations.Nullable;

import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** A tolerant lexer: incomplete input remains editable while LSP reports errors. */
public final class DotoriLexer extends LexerBase {
    public static final IElementType COMMENT = token("COMMENT");
    public static final IElementType STRING = token("STRING");
    public static final IElementType NUMBER = token("NUMBER");
    public static final IElementType KEYWORD = token("KEYWORD");
    public static final IElementType IDENTIFIER = token("IDENTIFIER");
    public static final IElementType BRACKET = token("BRACKET");
    public static final IElementType OPERATOR = token("OPERATOR");
    private static final Set<String> KEYWORDS = Set.of(
            "project", "package", "sources", "headers", "dependencies", "defines",
            "includes", "flags", "links", "copy", "unity-build", "pch", "output",
            "assembler", "option", "authors", "exports", "true", "false");
    private static final Pattern TOKENS = Pattern.compile(
            "(\\s+)|(#([^\\r\\n]*)|//([^\\r\\n]*)|\\(\\*(?s:.*?)(?:\\*\\)|\\z))"
            + "|(\"(?:[^\"\\\\]|\\\\(?s:.))*(?:\"|\\\\?\\z))"
            + "|([0-9]+)|([a-zA-Z_][a-zA-Z0-9_+\\-]*)|([{}\\[\\]()])|([=.,:])|((?s:.))");
    private CharSequence buffer = "";
    private Matcher matcher;
    private int end;
    private int tokenStart;
    private int tokenEnd;
    private IElementType type;

    private static IElementType token(String name) {
        return new IElementType(name, DotoriLanguage.INSTANCE);
    }

    @Override
    public void start(@NotNull CharSequence buffer, int startOffset, int endOffset, int initialState) {
        this.buffer = buffer;
        end = endOffset;
        tokenEnd = startOffset;
        matcher = TOKENS.matcher(buffer).region(startOffset, endOffset);
        advance();
    }

    @Override public int getState() { return 0; }
    @Override public @Nullable IElementType getTokenType() { return type; }
    @Override public int getTokenStart() { return tokenStart; }
    @Override public int getTokenEnd() { return tokenEnd; }
    @Override public @NotNull CharSequence getBufferSequence() { return buffer; }
    @Override public int getBufferEnd() { return end; }

    @Override
    public void advance() {
        tokenStart = tokenEnd;
        if (tokenStart == end || !matcher.find()) {
            type = null;
            return;
        }
        tokenEnd = matcher.end();
        if (matcher.group(1) != null) type = TokenType.WHITE_SPACE;
        else if (matcher.group(2) != null) type = COMMENT;
        else if (matcher.group(5) != null) type = STRING;
        else if (matcher.group(6) != null) type = NUMBER;
        else if (matcher.group(7) != null) type = KEYWORDS.contains(matcher.group()) ? KEYWORD : IDENTIFIER;
        else if (matcher.group(8) != null) type = BRACKET;
        else if (matcher.group(9) != null) type = OPERATOR;
        else type = TokenType.BAD_CHARACTER;
    }
}
