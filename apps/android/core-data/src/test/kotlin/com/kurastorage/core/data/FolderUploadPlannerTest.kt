@file:Suppress("MaxLineLength")

package com.kurastorage.core.data

import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class FolderUploadPlannerTest {
    @Test
    fun `planner preserves root nested and empty folders before their files`() =
        runTest {
            val source =
                FakeFolderTree(
                    root = folder("root", "Selected"),
                    children =
                        mapOf(
                            "root" to listOf(folder("empty", "Empty"), folder("nested", "Nested"), file("top", "top.txt")),
                            "empty" to emptyList(),
                            "nested" to listOf(file("child", "child.txt")),
                        ),
                )

            val plan = FolderUploadPlanner(source).plan("content://tree")

            assertEquals(
                listOf("Selected", "Selected/Empty", "Selected/Nested", "Selected/top.txt", "Selected/Nested/child.txt"),
                plan.entries.map { it.relativeSegments.joinToString("/") },
            )
            assertEquals(listOf(true, true, true, false, false), plan.entries.map { it is FolderUploadEntry.Folder })
            assertEquals(emptyList<FolderUploadRejection>(), plan.rejections)
        }

    @Test
    fun `invalid id name unreadable outside and duplicate children are rejected independently`() =
        runTest {
            val source =
                FakeFolderTree(
                    root = folder("root", "Selected"),
                    children =
                        mapOf(
                            "root" to
                                listOf(
                                    file("ok", "ok.txt"),
                                    file("", "invalid-id.txt"),
                                    file("bad-name", ".."),
                                    file("locked", "locked.txt", readable = false),
                                    file("outside", "outside.txt", withinTree = false),
                                    file("ok", "duplicate.txt"),
                                ),
                        ),
                )

            val plan = FolderUploadPlanner(source).plan("content://tree")

            assertEquals(listOf("Selected", "Selected/ok.txt"), plan.entries.map { it.relativeSegments.joinToString("/") })
            assertEquals(
                listOf(
                    FolderUploadFailure.INVALID_DOCUMENT_ID,
                    FolderUploadFailure.INVALID_NAME,
                    FolderUploadFailure.UNREADABLE,
                    FolderUploadFailure.OUTSIDE_TREE,
                    FolderUploadFailure.DUPLICATE_DOCUMENT,
                ),
                plan.rejections.map { it.reason },
            )
        }

    @Test
    fun `root failure depth and item bounds stop the whole plan safely`() =
        runTest {
            assertThrows(IllegalArgumentException::class.java) {
                kotlinx.coroutines.runBlocking {
                    FolderUploadPlanner(FakeFolderTree(file("root", "not-folder"), emptyMap())).plan("tree")
                }
            }
            val deep =
                FakeFolderTree(
                    folder("root", "Root"),
                    mapOf("root" to listOf(folder("one", "One")), "one" to listOf(folder("two", "Two"))),
                )
            assertThrows(IllegalArgumentException::class.java) {
                kotlinx.coroutines.runBlocking { FolderUploadPlanner(deep, maximumDepth = 1).plan("tree") }
            }
            assertThrows(IllegalArgumentException::class.java) {
                kotlinx.coroutines.runBlocking { FolderUploadPlanner(deep, maximumItems = 1).plan("tree") }
            }
        }

    private class FakeFolderTree(
        private val root: FolderUploadDocument,
        private val children: Map<String, List<FolderUploadDocument>>,
    ) : FolderUploadTreeSource {
        override suspend fun root(treeUri: String) = root

        override suspend fun children(
            treeUri: String,
            parentDocumentId: String,
        ) = children[parentDocumentId].orEmpty()
    }

    private fun folder(
        id: String,
        name: String,
    ) = FolderUploadDocument(id, name, true, 0, null, "content://$id", readable = true, withinTree = true)

    private fun file(
        id: String,
        name: String,
        readable: Boolean = true,
        withinTree: Boolean = true,
    ) = FolderUploadDocument(id, name, false, 4, "text/plain", "content://$id", readable, withinTree)
}
