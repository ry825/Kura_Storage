package com.kurastorage.core.data

import android.content.ContentValues
import android.os.Environment
import android.provider.MediaStore
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class AndroidContentStreamProviderTest {
    @Test
    fun contentUriCanBeStreamedForSafUploadAndDownloadAndDeleted() {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val resolver = context.contentResolver
        val uri =
            checkNotNull(
                resolver.insert(
                    MediaStore.Downloads.EXTERNAL_CONTENT_URI,
                    ContentValues().apply {
                        put(MediaStore.Downloads.DISPLAY_NAME, "kurastorage-saf-test.txt")
                        put(MediaStore.Downloads.MIME_TYPE, "text/plain")
                        put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS)
                    },
                ),
            )
        val provider = AndroidContentStreamProvider(context)
        try {
            provider.openOutput(uri.toString())!!.use { it.write("streamed".toByteArray()) }
            val content = provider.openInput(uri.toString())!!.use { it.readBytes() }

            assertArrayEquals("streamed".toByteArray(), content)
            val intent = provider.openIntent(uri.toString(), "text/plain")
            assertEquals(uri, intent.data)
            assertTrue(provider.delete(uri.toString()))
        } finally {
            resolver.delete(uri, null, null)
        }
    }
}
