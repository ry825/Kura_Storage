package com.kurastorage.core.network

import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import retrofit2.Response
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.POST
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

private interface KuraStorageService {
    @GET("system/health")
    suspend fun health(): Response<SystemHealthDto>

    @POST("auth/register-device")
    suspend fun registerDevice(
        @Body request: RegisterDeviceRequestDto,
    ): Response<TokenResponseDto>

    @POST("auth/login")
    suspend fun login(
        @Body request: LoginRequestDto,
    ): Response<TokenResponseDto>

    @POST("auth/refresh")
    suspend fun refresh(
        @Body request: RefreshRequestDto,
    ): Response<TokenResponseDto>

    @POST("auth/logout")
    suspend fun logout(
        @Header("Authorization") authorization: String,
        @Body request: LogoutRequestDto,
    ): Response<Unit>
}

class KuraStorageApi(
    baseUrl: String,
    client: OkHttpClient,
    private val json: Json = Json { ignoreUnknownKeys = false },
) : AuthenticationApi {
    private val service =
        Retrofit
            .Builder()
            .baseUrl("${baseUrl.trimEnd('/')}/")
            .client(client)
            .addConverterFactory(json.asConverterFactory(JSON_MEDIA_TYPE))
            .build()
            .create(KuraStorageService::class.java)

    suspend fun health(): SystemHealthDto = execute { service.health() }

    @Suppress("MaxLineLength")
    override suspend fun registerDevice(request: RegisterDeviceRequestDto): TokenResponseDto = execute { service.registerDevice(request) }

    override suspend fun login(request: LoginRequestDto): TokenResponseDto = execute { service.login(request) }

    override suspend fun refresh(request: RefreshRequestDto): TokenResponseDto = execute { service.refresh(request) }

    override suspend fun logout(
        accessToken: String,
        request: LogoutRequestDto,
    ) {
        executeNoContent { service.logout("Bearer $accessToken", request) }
    }

    private suspend fun <ResponseType : Any> execute(call: suspend () -> Response<ResponseType>): ResponseType =
        withContext(Dispatchers.IO) {
            try {
                val response = call()
                if (!response.isSuccessful) {
                    throw apiException(response.code(), response.errorBody()?.string().orEmpty())
                }
                response.body() ?: throw SerializationException("Successful API response had no body")
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

    private suspend fun executeNoContent(call: suspend () -> Response<Unit>) =
        withContext(Dispatchers.IO) {
            try {
                val response = call()
                if (!response.isSuccessful) {
                    throw apiException(response.code(), response.errorBody()?.string().orEmpty())
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
