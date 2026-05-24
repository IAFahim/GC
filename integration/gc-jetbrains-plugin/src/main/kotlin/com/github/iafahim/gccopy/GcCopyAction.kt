package com.github.iafahim.gccopy

import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import java.io.File
import java.util.concurrent.TimeUnit

class GcCopyAction : AnAction() {

    private val notificationGroupId = "gc.notifications"
    private val pathPrefix = System.getProperty("user.home").let { home ->
        "$home/.local/bin:$home/bin:/usr/local/bin"
    }

    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(event: AnActionEvent) {
        val dir = targetDirectory(event)
        event.presentation.isVisible = true
        event.presentation.isEnabled = dir != null
        if (dir != null) {
            event.presentation.text = "Copy with gc (${dir.name})"
        } else {
            event.presentation.text = "Copy with gc (No directory selected)"
        }
    }

    override fun actionPerformed(event: AnActionEvent) {
        val project = event.project
        val directory = targetDirectory(event) ?: return

        val invocation = GcCopySettings.instance.gcCommand

        ApplicationManager.getApplication().executeOnPooledThread {
            val outcome = runGc(directory, invocation)
            ApplicationManager.getApplication().invokeLater {
                announce(project, outcome)
            }
        }
    }

    private fun targetDirectory(event: AnActionEvent): File? {
        val selected: VirtualFile? = event.getData(CommonDataKeys.VIRTUAL_FILE)
        
        if (selected != null) {
            val folder = if (selected.isDirectory) selected else selected.parent
            if (folder != null) {
                return File(folder.path)
            }
        }
        
        // Fallback: If no file is selected (e.g. clicking empty space or a virtual node), 
        // try to use the project's base directory.
        val project = event.project
        if (project != null && project.basePath != null) {
            return File(project.basePath!!)
        }
        
        return null
    }

    private fun runGc(directory: File, invocation: String): Outcome {
        val command = "export PATH=\"$pathPrefix:\$PATH\"; $invocation"
        val process = ProcessBuilder("bash", "-lc", command)
            .directory(directory)
            .redirectErrorStream(true)
            .start()
        val transcript = process.inputStream.bufferedReader().readText().trim()
        val finished = process.waitFor(60, TimeUnit.SECONDS)
        return when {
            !finished -> {
                process.destroyForcibly()
                Outcome.Failure(directory, "gc timed out")
            }
            process.exitValue() == 0 -> Outcome.Success(directory)
            else -> Outcome.Failure(directory, transcript.ifBlank { "gc exited ${process.exitValue()}" })
        }
    }

    private fun announce(project: Project?, outcome: Outcome) {
        val group = NotificationGroupManager.getInstance().getNotificationGroup(notificationGroupId)
        val notification = when (outcome) {
            is Outcome.Success -> group.createNotification(
                "Copied ${outcome.directory.name} to clipboard",
                NotificationType.INFORMATION,
            )
            is Outcome.Failure -> group.createNotification(
                "gc failed in ${outcome.directory.name}",
                outcome.detail,
                NotificationType.ERROR,
            )
        }
        notification.notify(project)
    }

    private sealed interface Outcome {
        data class Success(val directory: File) : Outcome
        data class Failure(val directory: File, val detail: String) : Outcome
    }
}
