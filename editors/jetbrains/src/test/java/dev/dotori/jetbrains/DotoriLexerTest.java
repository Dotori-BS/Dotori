package dev.dotori.jetbrains;

import com.intellij.psi.TokenType;
import com.intellij.psi.tree.IElementType;
import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

import static org.junit.Assert.*;

public class DotoriLexerTest {
    private List<IElementType> lex(String text) {
        DotoriLexer lexer = new DotoriLexer();
        lexer.start(text);
        List<IElementType> types = new ArrayList<>();
        int offset = 0;
        while (lexer.getTokenType() != null) {
            assertEquals("No gaps or overlapping tokens", offset, lexer.getTokenStart());
            assertTrue("Lexer must make progress", lexer.getTokenEnd() > offset);
            offset = lexer.getTokenEnd();
            if (lexer.getTokenType() != TokenType.WHITE_SPACE) types.add(lexer.getTokenType());
            lexer.advance();
        }
        assertEquals(text.length(), offset);
        return types;
    }

    @Test public void projectWithHyphenatedPropertiesAndConditions() {
        assertEquals(List.of(DotoriLexer.KEYWORD, DotoriLexer.IDENTIFIER, DotoriLexer.BRACKET,
                DotoriLexer.BRACKET, DotoriLexer.IDENTIFIER, DotoriLexer.OPERATOR,
                DotoriLexer.IDENTIFIER, DotoriLexer.BRACKET, DotoriLexer.BRACKET,
                DotoriLexer.IDENTIFIER, DotoriLexer.OPERATOR, DotoriLexer.IDENTIFIER,
                DotoriLexer.BRACKET, DotoriLexer.BRACKET),
                lex("project demo { [windows.debug] { cxx-standard = c++23 } }"));
    }

    @Test public void commentsDoNotConsumeStringsOrFollowingCode() {
        assertEquals(List.of(DotoriLexer.COMMENT, DotoriLexer.STRING, DotoriLexer.COMMENT,
                DotoriLexer.COMMENT, DotoriLexer.KEYWORD, DotoriLexer.NUMBER),
                lex("# header\n\"https://host/#name\\\"\" // line\n(* multi\nline *) true 42"));
    }

    @Test public void unfinishedInputAndUnicodeAreSafe() {
        assertEquals(List.of(DotoriLexer.STRING), lex("\"한글 😀\\"));
        assertEquals(List.of(DotoriLexer.COMMENT), lex("(* unfinished\ncomment"));
        assertEquals(List.of(TokenType.BAD_CHARACTER, DotoriLexer.KEYWORD), lex("@ project"));
        assertTrue(lex("").isEmpty());
    }

    @Test public void restartAtTokenBoundaryUsesRequestedRange() {
        DotoriLexer lexer = new DotoriLexer();
        lexer.start("ignored true ignored", 8, 12, 0);
        assertSame(DotoriLexer.KEYWORD, lexer.getTokenType());
        assertEquals(8, lexer.getTokenStart());
        assertEquals(12, lexer.getTokenEnd());
        lexer.advance();
        assertNull(lexer.getTokenType());
    }
}
