package com.kurastorage.core.security

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertFalse
import org.junit.Test
import javax.crypto.KeyGenerator

class AesGcmCodecTest {
    @Test
    fun `AES GCM encrypts and authenticates refresh token`() {
        val key = KeyGenerator.getInstance("AES").apply { init(256) }.generateKey()
        val plaintext = "refresh-token-never-store-this-plaintext".encodeToByteArray()

        val encrypted = AesGcmCodec.encrypt(key, plaintext)

        assertFalse(encrypted.toList().windowed(plaintext.size).any { it == plaintext.toList() })
        assertArrayEquals(plaintext, AesGcmCodec.decrypt(key, encrypted))
    }

    @Test(expected = Exception::class)
    fun `tampered ciphertext is rejected`() {
        val key = KeyGenerator.getInstance("AES").apply { init(256) }.generateKey()
        val encrypted = AesGcmCodec.encrypt(key, "refresh-token".encodeToByteArray())
        encrypted[encrypted.lastIndex] = (encrypted.last() + 1).toByte()

        AesGcmCodec.decrypt(key, encrypted)
    }
}
