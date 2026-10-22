package dev.dotori.jetbrains;

import com.intellij.execution.configurations.GeneralCommandLine;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.vfs.VirtualFile;
import com.intellij.platform.lsp.api.LspIntegrationProvider;
import com.intellij.platform.lsp.api.ProjectWideLspClientDescriptor;
import org.jetbrains.annotations.NotNull;

public final class DotoriLspIntegrationProvider implements LspIntegrationProvider {
    @Override
    public void fileOpened(
            @NotNull Project project,
            @NotNull VirtualFile file,
            @NotNull LspClientStarter clientStarter) {
        if (isDotoriFile(file)) {
            clientStarter.ensureClientStarted(new DotoriLspClientDescriptor(project));
        }
    }

    private static boolean isDotoriFile(VirtualFile file) {
        return file.getFileType() == DotoriFileType.INSTANCE;
    }

    private static final class DotoriLspClientDescriptor extends ProjectWideLspClientDescriptor {
        private final Project dotoriProject;

        private DotoriLspClientDescriptor(Project project) {
            super(project, "Dotori");
            this.dotoriProject = project;
        }

        @Override
        public boolean isSupportedFile(@NotNull VirtualFile file) {
            return isDotoriFile(file);
        }

        @Override
        public @NotNull GeneralCommandLine createCommandLine() {
            return new GeneralCommandLine(DotoriSettings.getInstance().executable, "lsp")
                    .withParentEnvironmentType(GeneralCommandLine.ParentEnvironmentType.CONSOLE)
                    .withWorkDirectory(dotoriProject.getBasePath());
        }
    }
}
