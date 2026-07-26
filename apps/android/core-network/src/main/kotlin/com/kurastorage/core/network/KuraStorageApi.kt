package com.kurastorage.core.network

import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.IOException
import javax.net.ssl.SSLException

interface AuthenticationApi {
    suspend fun registerDevice(request: RegisterDeviceRequestDto): TokenResponseDto

    suspend fun login(request: LoginRequestDto): TokenResponseDto

    suspend fun refresh(request: RefreshRequestDto): TokenResponseDto

    suspend fun logout(
        accessToken: String,
        request: LogoutRequestDto,
    )
}

class KuraStorageApi(
    private val baseUrl: String,
    private val client: OkHttpClient,
    private val json: Json = Json { ignoreUnknownKeys = false },
) : AuthenticationApi {
    suspend fun health(): SystemHealthDto =
        executeJson(
            Request
                .Builder()
                .url("$baseUrl/system/health")
                .get()
                .build(),
        )

    @Suppress("MaxLineLength")
    override suspend fun registerDevice(request: RegisterDeviceRequestDto): TokenResponseDto = postJson("auth/register-device", request)

    override suspend fun login(request: LoginRequestDto): TokenResponseDto = postJson("auth/login", request)

    override suspend fun refresh(request: RefreshRequestDto): TokenResponseDto = postJson("auth/refresh", request)

    override suspend fun logout(
        accessToken: String,
        request: LogoutRequestDto,
    ) {
        val httpRequest =
            Request
                .Builder()
                .url("$baseUrl/auth/logout")
                .header("Authorization", "Bearer $accessToken")
                .post(json.encodeToString(request).toRequestBody(JSON_MEDIA_TYPE))
                .build()
        executeNoContent(httpRequest)
    }

    private suspend inline fun <reified RequestType, reified ResponseType> postJson(
        path: String,
        request: RequestType,
    ): ResponseType {
        val httpRequest =
            Request
                .Builder()
                .url("$baseUrl/$path")
                .post(json.encodeToString(request).toRequestBody(JSON_MEDIA_TYPE))
                .build()
        return executeJson(httpRequest)
    }

    private suspend inline fun <reified ResponseType> executeJson(request: Request): ResponseType =
        withContext(Dispatchers.IO) {
            try {
                client.newCall(request).execute().use { response ->
                    if (!response.isSuccessful) throw apiException(response.code, response.body.string())
                    json.decodeFromString(response.body.string())
                }
            } catch (error: KuraStorageException) {
                throw error
            } catch (error: SSLException) {
                throw error
            } catch (error: IOException) {
                throw KuraStorageException.Network(error)
            } catch (error: SerializationException) {
                throw KuraStorageException.Network(error)
            }
        }

    private suspend fun executeNoContent(request: Request) =
        withContext(Dispatchers.IO) {
            try {
                client.newCall(request).execute().use { response ->
                    if (!response.isSuccessful) throw apiException(response.code, response.body.string())
                }
            } catch (error: KuraStorageException) {
                throw error
            } catch (error: IOException) {
                throw KuraStorageException.Network(error)
            }
        }

    private fun apiException(
        statusCode: Int,
        body: String,
    ): KuraStorageException.Api {
        val response = runCatching { json.decodeFromString<ErrorResponseDto>(body) }.getOrNull()
        val code = response?.code?.let { runCatching { ErrorCode.valueOf(it) }.getOrNull() } ?: ErrorCode.UNKNOWN
        return KuraStorageException.Api(ApiError(code, response?.requestId, statusCode))
    }

    private companion object {
        val JSON_MEDIA_TYPE = "application/json; charset=utf-8".toMediaType()
    }
}
