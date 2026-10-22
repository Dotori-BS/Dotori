package dev.dotori.jetbrains;

import com.intellij.openapi.options.Configurable;
import org.jetbrains.annotations.Nullable;

import javax.swing.*;
import java.awt.BorderLayout;

public final class DotoriConfigurable implements Configurable {
    private JTextField executable;

    @Override
    public String getDisplayName() {
        return "Dotori";
    }

    @Override
    public @Nullable JComponent createComponent() {
        executable = new JTextField(40);
        JPanel row = new JPanel(new BorderLayout(8, 0));
        JLabel label = new JLabel("Dotori executable:");
        label.setLabelFor(executable);
        row.add(label, BorderLayout.WEST);
        row.add(executable, BorderLayout.CENTER);
        JPanel panel = new JPanel(new BorderLayout(0, 8));
        panel.add(row, BorderLayout.NORTH);
        panel.add(new JLabel("Use dotori from PATH or an absolute path. Restart the Dotori language server after applying."),
                BorderLayout.CENTER);
        reset();
        return panel;
    }

    private String value() {
        String value = executable.getText().trim();
        return value.isEmpty() ? "dotori" : value;
    }

    @Override
    public boolean isModified() {
        return executable != null && !value().equals(DotoriSettings.getInstance().executable);
    }

    @Override
    public void apply() {
        DotoriSettings.getInstance().executable = value();
    }

    @Override
    public void reset() {
        if (executable != null) executable.setText(DotoriSettings.getInstance().executable);
    }

    @Override
    public void disposeUIResources() {
        executable = null;
    }
}
