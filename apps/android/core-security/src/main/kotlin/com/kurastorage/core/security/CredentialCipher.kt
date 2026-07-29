package com.kurastorage.core.security

interface CredentialCipher {
    fun encrypt(plaintext: ByteArray): ByteArray

    fun decrypt(ciphertext: ByteArray): ByteArray

    fun deleteKey()
}

interface EncryptedTokenStore {
    fun read(): String?

    fun write(refreshToken: String)

    fun clear()
}
