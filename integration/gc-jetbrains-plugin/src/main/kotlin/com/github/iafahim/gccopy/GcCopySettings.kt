package com.github.iafahim.gccopy

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.util.xmlb.XmlSerializerUtil

@State(
    name = "com.github.iafahim.gccopy.GcCopySettings",
    storages = [Storage("GcCopySettings.xml")]
)
class GcCopySettings : PersistentStateComponent<GcCopySettings> {
    var gcCommand: String = "gc -e cs -z // \\n\\n --force"

    override fun getState(): GcCopySettings = this

    override fun loadState(state: GcCopySettings) {
        XmlSerializerUtil.copyBean(state, this)
    }

    companion object {
        val instance: GcCopySettings
            get() = ApplicationManager.getApplication().getService(GcCopySettings::class.java)
    }
}
