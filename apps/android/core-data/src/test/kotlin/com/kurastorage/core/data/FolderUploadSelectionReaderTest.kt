package com.kurastorage.core.data

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class FolderUploadSelectionReaderTest {
    @Test
    fun `cancelled picker does not acquire permission or walk tree`() =
        runTest {
            var permissionCalls = 0
            var plannerCalls = 0
            val reader =
                FolderUploadSelectionReader(
                    permissionSource = { permissionCalls++ },
                    plannerFactory = {
                        plannerCalls++
                        plannerReturning()
                    },
                )

            assertEquals(FolderUploadSelectionResult.Cancelled, reader.read(null))
            assertEquals(0, permissionCalls)
            assertEquals(0, plannerCalls)
        }

    @Test
    fun `permission is retained before the selected tree is walked`() =
        runTest {
            val calls = mutableListOf<String>()
            val reader =
                FolderUploadSelectionReader(
                    permissionSource = { calls += "permission" },
                    plannerFactory = {
                        calls += "planner"
                        plannerReturning { calls += "walk" }
                    },
                )

            assertEquals(FolderUploadSelectionResult.Ready(emptyPlan), reader.read("content://tree/root"))
            assertEquals(listOf("permission", "planner", "walk"), calls)
        }

    @Test
    fun `permission and traversal failures are returned without hiding cancellation`() =
        runTest {
            val denied = FolderUploadSelectionReader({ throw SecurityException() }) { plannerReturning() }
            assertEquals(
                FolderUploadSelectionResult.Rejected("The selected folder permission could not be retained."),
                denied.read("content://tree/root"),
            )

            val cancelled = FolderUploadSelectionReader({}) { plannerThrowing(CancellationException()) }
            assertThrows(CancellationException::class.java) {
                kotlinx.coroutines.runBlocking { cancelled.read("content://tree/root") }
            }
        }

    private fun plannerReturning(onWalk: () -> Unit = {}) =
        FolderUploadPlanner(
            object : FolderUploadTreeSource {
                override suspend fun root(treeUri: String): FolderUploadDocument {
                    onWalk()
                    return FolderUploadDocument("root", "Root", true, 0, null, treeUri, true, true)
                }

                override suspend fun children(
                    treeUri: String,
                    parentDocumentId: String,
                ) = emptyList<FolderUploadDocument>()
            },
        )

    private fun plannerThrowing(error: Throwable) =
        FolderUploadPlanner(
            object : FolderUploadTreeSource {
                override suspend fun root(treeUri: String): FolderUploadDocument = throw error

                override suspend fun children(
                    treeUri: String,
                    parentDocumentId: String,
                ) = emptyList<FolderUploadDocument>()
            },
        )

    private companion object {
        val emptyPlan =
            FolderUploadPlan(
                entries = listOf(FolderUploadEntry.Folder("root", listOf("Root"))),
                rejections = emptyList(),
            )
    }
}
