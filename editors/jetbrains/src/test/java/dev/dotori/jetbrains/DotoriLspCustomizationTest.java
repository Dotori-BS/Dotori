package dev.dotori.jetbrains;

import com.intellij.openapi.fileTypes.PlainTextFileType;
import com.intellij.openapi.fileTypes.FileType;
import com.intellij.platform.lsp.api.customization.LspFormattingSupport;
import com.intellij.testFramework.LightVirtualFile;
import org.junit.Test;

import static org.junit.Assert.*;

public class DotoriLspCustomizationTest {
    @Test public void staticCapabilityEnablesLspFormattingForDotfilesAndNamedFiles() {
        LspFormattingSupport formatting = (LspFormattingSupport) new DotoriLspCustomization().getFormattingCustomizer();
        for (String name : new String[] { ".dotori", "named.dotori" }) {
            // Without an IDE application, EmptyFileTypeRegistry substitutes a placeholder
            // for language file types. Model the file type registered by plugin.xml explicitly.
            LightVirtualFile file = new LightVirtualFile(name, DotoriFileType.INSTANCE, "") {
                @Override public FileType getFileType() { return DotoriFileType.INSTANCE; }
            };
            assertSame(name, DotoriFileType.INSTANCE, file.getFileType());
            assertTrue(formatting.shouldFormatThisFileExclusivelyByServer(file, false, false));
            assertTrue(formatting.shouldFormatThisFileExclusivelyByServer(file, true, false));
        }
    }

    @Test public void otherLanguagesDoNotUseDotoriFormatting() {
        LspFormattingSupport formatting = (LspFormattingSupport) new DotoriLspCustomization().getFormattingCustomizer();
        LightVirtualFile file = new LightVirtualFile("readme.txt", PlainTextFileType.INSTANCE, "");
        assertFalse(formatting.shouldFormatThisFileExclusivelyByServer(file, false, false));
    }

    @Test public void enablesOnlyDotoriFilesForFormatOnSaveByDefault() {
        var fileTypes = new DotoriFormatOnSaveDefaultsProvider().getFileTypesFormattedOnSaveByDefault();
        assertEquals(1, fileTypes.size());
        assertSame(DotoriFileType.INSTANCE, fileTypes.iterator().next());
    }
}
