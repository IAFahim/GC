package com.github.iafahim.gccopy

import com.intellij.openapi.options.Configurable
import com.intellij.ui.components.JBLabel
import com.intellij.ui.components.JBTextField
import com.intellij.util.ui.FormBuilder
import javax.swing.JComponent
import javax.swing.JPanel

class GcCopyConfigurable : Configurable {
    private var myComponent: JPanel? = null
    private var gcCommandField: JBTextField? = null

    override fun getDisplayName(): String = "gc Copy"

    override fun createComponent(): JComponent? {
        gcCommandField = JBTextField()
        myComponent = FormBuilder.createFormBuilder()
            .addLabeledComponent(JBLabel("gc command line:"), gcCommandField!!, 1, false)
            .addComponentFillVertically(JPanel(), 0)
            .panel
        return myComponent
    }

    override fun isModified(): Boolean {
        val settings = GcCopySettings.instance
        return gcCommandField?.text != settings.gcCommand
    }

    override fun apply() {
        val settings = GcCopySettings.instance
        settings.gcCommand = gcCommandField?.text ?: ""
    }

    override fun reset() {
        val settings = GcCopySettings.instance
        gcCommandField?.text = settings.gcCommand
    }

    override fun disposeUIResources() {
        myComponent = null
        gcCommandField = null
    }
}
