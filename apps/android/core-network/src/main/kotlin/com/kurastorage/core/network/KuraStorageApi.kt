package com.kurastorage.core.network

import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.RequestBody
import okhttp3.ResponseBody
import retrofit2.Response
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.Multipart
import retrofit2.http.POST
import retrofit2.http.Part
import retrofit2.http.Path
import retrofit2.http.Query
import retrofit2.http.Streaming
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

sealed interface NetworkCallResult<out T> {
    data class Success<T>(
        val value: T,
    ) : NetworkCallResult<T>

    data object Unauthorized : NetworkCallResult<Nothing>
}

interface FileApi {
    suspend fun listFiles(
        accessToken: String,
        parentId: String?,
        page: Int,
        pageSize: Int,
    ): NetworkCallResult<FileEntryPageDto>

    suspend fun getFile(
        accessToken: String,
        fileId: String,
    ): NetworkCallResult<FileEntryDto>

    suspend fun createFolder(
        accessToken: String,
        request: CreateFolderRequestDto,
    ): NetworkCallResult<FileEntryDto>

    suspend fun trash(
        accessToken: String,
        fileId: String,
    ): NetworkCallResult<FileEntryDto>

    suspend fun listTrash(
        accessToken: String,
        page: Int,
        pageSize: Int,
    ): NetworkCallResult<FileEntryPageDto>

    suspend fun restore(
        accessToken: String,
        fileId: String,
    ): NetworkCallResult<FileEntryDto>

    @Suppress("LongParameterList")
    suspend fun upload(
        accessToken: String,
        idempotencyKey: String,
        destinationFolderId: RequestBody,
        fileName: RequestBody,
        size: RequestBody,
        contentType: RequestBody?,
        sha256: RequestBody?,
        file: MultipartBody.Part,
    ): NetworkCallResult<FileEntryDto>

    suspend fun download(
        accessToken: String,
        fileId: String,
    ): NetworkCallResult<ResponseBody>
}

@Suppress("TooManyFunctions")
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

    @GET("files")
    suspend fun listFiles(
        @Header("Authorization") authorization: String,
        @Query("parentId") parentId: String?,
        @Query("page") page: Int,
        @Query("pageSize") pageSize: Int,
    ): Response<FileEntryPageDto>

    @GET("files/{fileId}")
    suspend fun getFile(
        @Header("Authorization") authorization: String,
        @Path("fileId") fileId: String,
    ): Response<FileEntryDto>

    @POST("folders")
    suspend fun createFolder(
        @Header("Authorization") authorization: String,
        @Body request: CreateFolderRequestDto,
    ): Response<FileEntryDto>

    @DELETE("files/{fileId}")
    suspend fun trash(
        @Header("Authorization") authorization: String,
        @Path("fileId") fileId: String,
    ): Response<FileEntryDto>

    @GET("trash")
    suspend fun listTrash(
        @Header("Authorization") authorization: String,
        @Query("page") page: Int,
        @Query("pageSize") pageSize: Int,
    ): Response<FileEntryPageDto>

    @POST("files/{fileId}/restore")
    suspend fun restore(
        @Header("Authorization") authorization: String,
        @Path("fileId") fileId: String,
    ): Response<FileEntryDto>

    @Suppress("LongParameterList")
    @Multipart
    @POST("files/upload")
    suspend fun upload(
        @Header("Authorization") authorization: String,
        @Header("Idempotency-Key") idempotencyKey: String,
        @Part("destinationFolderId") destinationFolderId: RequestBody,
        @Part("fileName") fileName: RequestBody,
        @Part("size") size: RequestBody,
        @Part("contentType") contentType: RequestBody?,
        @Part("sha256") sha256: RequestBody?,
        @Part file: MultipartBody.Part,
    ): Response<FileEntryDto>

    @Streaming
    @GET("files/{fileId}/content")
    suspend fun download(
        @Header("Authorization") authorization: String,
        @Path("fileId") fileId: String,
    ): Response<ResponseBody>
}

@Suppress("TooManyFunctions")
class KuraStorageApi(
    baseUrl: String,
    client: OkHttpClient,
    private val json: Json = Json { ignoreUnknownKeys = false },
) : AuthenticationApi,
    FileApi {
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

    override suspend fun listFiles(
        accessToken: String,
        parentId: String?,
        page: Int,
        pageSize: Int,
    ) = executeAuthenticated { service.listFiles(bearer(accessToken), parentId, page, pageSize) }

    override suspend fun getFile(
        accessToken: String,
        fileId: String,
    ) = executeAuthenticated { service.getFile(bearer(accessToken), fileId) }

    override suspend fun createFolder(
        accessToken: String,
        request: CreateFolderRequestDto,
    ) = executeAuthenticated { service.createFolder(bearer(accessToken), request) }

    override suspend fun trash(
        accessToken: String,
        fileId: String,
    ) = executeAuthenticated { service.trash(bearer(accessToken), fileId) }

    override suspend fun listTrash(
        accessToken: String,
        page: Int,
        pageSize: Int,
    ) = executeAuthenticated { service.listTrash(bearer(accessToken), page, pageSize) }

    override suspend fun restore(
        accessToken: String,
        fileId: String,
    ) = executeAuthenticated { service.restore(bearer(accessToken), fileId) }

    @Suppress("LongParameterList")
    override suspend fun upload(
        accessToken: String,
        idempotencyKey: String,
        destinationFolderId: RequestBody,
        fileName: RequestBody,
        size: RequestBody,
        contentType: RequestBody?,
        sha256: RequestBody?,
        file: MultipartBody.Part,
    ) = executeAuthenticated {
        service.upload(
            bearer(accessToken),
            idempotencyKey,
            destinationFolderId,
            fileName,
            size,
            contentType,
            sha256,
            file,
        )
    }

    override suspend fun download(
        accessToken: String,
        fileId: String,
    ) = executeAuthenticated { service.download(bearer(accessToken), fileId) }

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

    private suspend fun <ResponseType : Any> executeAuthenticated(
        call: suspend () -> Response<ResponseType>,
    ): NetworkCallResult<ResponseType> =
        withContext(Dispatchers.IO) {
            try {
                val response = call()
                if (response.code() == HTTP_UNAUTHORIZED) return@withContext NetworkCallResult.Unauthorized
                if (!response.isSuccessful) {
                    throw apiException(response.code(), response.errorBody()?.string().orEmpty())
                }
                NetworkCallResult.Success(
                    response.body() ?: throw SerializationException("Successful API response had no body"),
                )
            } catch (error: KuraStorageException) {
                throw error
            } catch (error: IOException) {
                throw KuraStorageException.Network(error)
            }
        }

    private fun bearer(accessToken: String) = "Bearer $accessToken"

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
        const val HTTP_UNAUTHORIZED = 401
    }
}
