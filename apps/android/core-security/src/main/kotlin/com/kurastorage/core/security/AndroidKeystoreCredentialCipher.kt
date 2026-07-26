package com.kurastorage.core.security

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.security.keystore.StrongBoxUnavailableException
import android.util.Base64
import com.kurastorage.core.model.KuraStorageException
import java.nio.ByteBuffer
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class AndroidKeystoreCredentialCipher : CredentialCipher {
    private val keyStore =
        KeyStore.getInstance(KEYSTORE_PROVIDER).apply {
            load(null)
        }

    @Suppress("TooGenericExceptionCaught")
    override fun encrypt(plaintext: ByteArray): ByteArray =
        try {
            AesGcmCodec.encrypt(getOrCreateKey(), plaintext)
        } catch (error: Exception) {
            throw KuraStorageException.CredentialUnavailable(error)
        }

    @Suppress("TooGenericExceptionCaught")
    override fun decrypt(ciphertext: ByteArray): ByteArray =
        try {
            val key = checkNotNull(keyStore.getKey(KEY_ALIAS, null) as? SecretKey)
            AesGcmCodec.decrypt(key, ciphertext)
        } catch (error: Exception) {
            throw KuraStorageException.CredentialUnavailable(error)
        }

    override fun deleteKey() {
        if (keyStore.containsAlias(KEY_ALIAS)) keyStore.deleteEntry(KEY_ALIAS)
    }

    private fun getOrCreateKey(): SecretKey {
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }
        return try {
            generateKey(useStrongBox = true)
        } catch (_: StrongBoxUnavailableException) {
            generateKey(useStrongBox = false)
        }
    }

    private fun generateKey(useStrongBox: Boolean): SecretKey {
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE_PROVIDER)
        val spec =
            KeyGenParameterSpec
                .Builder(
                    KEY_ALIAS,
                    KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
                ).setKeySize(KEY_SIZE_BITS)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setRandomizedEncryptionRequired(true)
                .setIsStrongBoxBacked(useStrongBox)
                .build()
        generator.init(spec)
        return generator.generateKey()
    }

    private companion object {
        const val KEYSTORE_PROVIDER = "AndroidKeyStore"
        const val KEY_ALIAS = "kurastorage.refresh-token.v1"
        const val KEY_SIZE_BITS = 256
    }
}

object AesGcmCodec {
    fun encrypt(
        key: SecretKey,
        plaintext: ByteArray,
    ): ByteArray {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, key)
        return ByteBuffer
            .allocate(Int.SIZE_BYTES + cipher.iv.size + cipher.getOutputSize(plaintext.size))
            .putInt(cipher.iv.size)
            .put(cipher.iv)
            .put(cipher.doFinal(plaintext))
            .array()
    }

    fun decrypt(
        key: SecretKey,
        ciphertext: ByteArray,
    ): ByteArray {
        val buffer = ByteBuffer.wrap(ciphertext)
        val ivSize = buffer.int
        require(ivSize in MIN_IV_SIZE..MAX_IV_SIZE)
        val iv = ByteArray(ivSize)
        buffer.get(iv)
        val encrypted = ByteArray(buffer.remaining())
        buffer.get(encrypted)
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(GCM_TAG_BITS, iv))
        return cipher.doFinal(encrypted)
    }

    private const val TRANSFORMATION = "AES/GCM/NoPadding"
    private const val GCM_TAG_BITS = 128
    private const val MIN_IV_SIZE = 12
    private const val MAX_IV_SIZE = 32
}

class SharedPreferencesEncryptedTokenStore(
    context: Context,
    private val cipher: CredentialCipher,
) : EncryptedTokenStore {
    private val preferences =
        context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    @Suppress("TooGenericExceptionCaught")
    override fun read(): String? {
        val encoded = preferences.getString(REFRESH_TOKEN_KEY, null) ?: return null
        return try {
            val encrypted = Base64.decode(encoded, Base64.NO_WRAP)
            cipher.decrypt(encrypted).decodeToString()
        } catch (error: Exception) {
            clear()
            throw KuraStorageException.CredentialUnavailable(error)
        }
    }

    override fun write(refreshToken: String) {
        val encrypted = cipher.encrypt(refreshToken.encodeToByteArray())
        preferences
            .edit()
            .putString(REFRESH_TOKEN_KEY, Base64.encodeToString(encrypted, Base64.NO_WRAP))
            .apply()
    }

    override fun clear() {
        preferences.edit().remove(REFRESH_TOKEN_KEY).apply()
        cipher.deleteKey()
    }

    private companion object {
        const val PREFERENCES_NAME = "kurastorage_secure_credentials"
        const val REFRESH_TOKEN_KEY = "encrypted_refresh_token"
    }
}
