package dev.dotori.jetbrains;

import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.components.PersistentStateComponent;
import com.intellij.openapi.components.State;
import com.intellij.openapi.components.Storage;
import com.intellij.util.xmlb.XmlSerializerUtil;
import org.jetbrains.annotations.NotNull;

@State(name = "DotoriSettings", storages = @Storage("dotori.xml"))
public final class DotoriSettings implements PersistentStateComponent<DotoriSettings> {
    public String executable = "dotori";

    public static DotoriSettings getInstance() {
        return ApplicationManager.getApplication().getService(DotoriSettings.class);
    }

    @Override
    public @NotNull DotoriSettings getState() {
        return this;
    }

    @Override
    public void loadState(@NotNull DotoriSettings state) {
        XmlSerializerUtil.copyBean(state, this);
    }
}
