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
import retrofit2.http.PATCH
import retrofit2.http.POST
import retrofit2.http.PUT
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

@Suppress("TooManyFunctions")
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

    suspend fun updateFile(
        accessToken: String,
        fileId: String,
        request: UpdateFileRequestDto,
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

    suspend fun recheckMissing(
        accessToken: String,
        fileId: String,
    ): NetworkCallResult<FileEntryDto> = error("Missing recheck is not implemented by this test double")

    suspend fun deleteMissingIndexEntry(
        accessToken: String,
        fileId: String,
    ): NetworkCallResult<Unit> = error("Missing index deletion is not implemented by this test double")

    suspend fun purge(
        accessToken: String,
        fileId: String,
        idempotencyKey: String,
    ): NetworkCallResult<Unit> = error("Purge is not implemented by this test double")

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

interface AdminStorageApi {
    suspend fun getAdminStorage(accessToken: String): NetworkCallResult<AdminStorageStatusDto>
}

interface UploadSessionApi {
    suspend fun createUploadSession(
        accessToken: String,
        idempotencyKey: String,
        request: CreateUploadSessionRequestDto,
    ): NetworkCallResult<UploadSessionDto>

    suspend fun getUploadSession(
        accessToken: String,
        sessionId: String,
    ): NetworkCallResult<UploadSessionDto>

    suspend fun uploadChunk(
        accessToken: String,
        sessionId: String,
        offset: Long,
        sha256: String,
        body: RequestBody,
    ): NetworkCallResult<UploadChunkDto>

    suspend fun completeUploadSession(
        accessToken: String,
        sessionId: String,
    ): NetworkCallResult<FileEntryDto>

    suspend fun cancelUploadSession(
        accessToken: String,
        sessionId: String,
    ): NetworkCallResult<Unit>
}

interface SharingApi {
    suspend fun listCandidates(accessToken: String): NetworkCallResult<List<ShareCandidateDto>>

    suspend fun createShare(
        accessToken: String,
        request: CreateShareRequestDto,
    ): NetworkCallResult<ShareItemDto>

    suspend fun listShares(
        accessToken: String,
        scope: String,
        targetType: String?,
        page: Int,
        pageSize: Int,
    ): NetworkCallResult<SharePageDto>

    suspend fun getShare(
        accessToken: String,
        shareId: String,
    ): NetworkCallResult<ShareItemDto>

    suspend fun setMember(
        accessToken: String,
        shareId: String,
        userId: String,
        request: SetShareMemberRequestDto,
    ): NetworkCallResult<ShareItemDto>

    suspend fun removeMember(
        accessToken: String,
        shareId: String,
        userId: String,
    ): NetworkCallResult<Unit>

    suspend fun deleteShare(
        accessToken: String,
        shareId: String,
    ): NetworkCallResult<Unit>
}

interface SearchApi {
    suspend fun search(
        accessToken: String,
        request: SearchRequestDto,
    ): NetworkCallResult<SearchPageDto>

    suspend fun listRecentFiles(
        accessToken: String,
        page: Int,
        pageSize: Int,
    ): NetworkCallResult<RecentFilePageDto>

    suspend fun recordRecentFile(
        accessToken: String,
        fileId: String,
    ): NetworkCallResult<Unit>
}

@Suppress("TooManyFunctions")
private interface KuraStorageService {
    @GET("system/health")
    suspend fun health(): Response<SystemHealthDto>

    @GET("shares/candidates")
    suspend fun listShareCandidates(
        @Header("Authorization") authorization: String,
    ): Response<List<ShareCandidateDto>>

    @POST("shares")
    suspend fun createShare(
        @Header("Authorization") authorization: String,
        @Body request: CreateShareRequestDto,
    ): Response<ShareItemDto>

    @GET("shares")
    suspend fun listShares(
        @Header("Authorization") authorization: String,
        @Query("scope") scope: String,
        @Query("targetType") targetType: String?,
        @Query("page") page: Int,
        @Query("pageSize") pageSize: Int,
    ): Response<SharePageDto>

    @GET("shares/{shareId}")
    suspend fun getShare(
        @Header("Authorization") authorization: String,
        @Path("shareId") shareId: String,
    ): Response<ShareItemDto>

    @PUT("shares/{shareId}/members/{userId}")
    suspend fun setShareMember(
        @Header("Authorization") authorization: String,
        @Path("shareId") shareId: String,
        @Path("userId") userId: String,
        @Body request: SetShareMemberRequestDto,
    ): Response<ShareItemDto>

    @DELETE("shares/{shareId}/members/{userId}")
    suspend fun removeShareMember(
        @Header("Authorization") authorization: String,
        @Path("shareId") shareId: String,
        @Path("userId") userId: String,
    ): Response<Unit>

    @DELETE("shares/{shareId}")
    suspend fun deleteShare(
        @Header("Authorization") authorization: String,
        @Path("shareId") shareId: String,
    ): Response<Unit>

    @GET("search")
    @Suppress("LongParameterList")
    suspend fun search(
        @Header("Authorization") authorization: String,
        @Query("q") query: String?,
        @Query("entryType") entryType: String?,
        @Query("fileCategory") fileCategory: String?,
        @Query("status") status: String?,
        @Query("updatedFrom") updatedFrom: String?,
        @Query("updatedTo") updatedTo: String?,
        @Query("minSize") minSize: Long?,
        @Query("maxSize") maxSize: Long?,
        @Query("ownerUserId") ownerUserId: String?,
        @Query("shareTargetId") shareTargetId: String?,
        @Query("page") page: Int,
        @Query("pageSize") pageSize: Int,
    ): Response<SearchPageDto>

    @GET("recent-files")
    suspend fun listRecentFiles(
        @Header("Authorization") authorization: String,
        @Query("page") page: Int,
        @Query("pageSize") pageSize: Int,
    ): Response<RecentFilePageDto>

    @PUT("recent-files/{fileId}")
    suspend fun recordRecentFile(
        @Header("Authorization") authorization: String,
        @Path("fileId") fileId: String,
    ): Response<Unit>

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

    @PATCH("files/{fileId}")
    suspend fun updateFile(
        @Header("Authorization") authorization: String,
        @Path("fileId") fileId: String,
        @Body request: UpdateFileRequestDto,
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

    @POST("files/{fileId}/missing/recheck")
    suspend fun recheckMissing(
        @Header("Authorization") authorization: String,
        @Path("fileId") fileId: String,
    ): Response<FileEntryDto>

    @DELETE("files/{fileId}/missing-index-entry")
    suspend fun deleteMissingIndexEntry(
        @Header("Authorization") authorization: String,
        @Path("fileId") fileId: String,
    ): Response<Unit>

    @DELETE("trash/{fileId}")
    suspend fun purge(
        @Header("Authorization") authorization: String,
        @Header("Idempotency-Key") idempotencyKey: String,
        @Path("fileId") fileId: String,
    ): Response<Unit>

    @GET("admin/storage")
    suspend fun getAdminStorage(
        @Header("Authorization") authorization: String,
    ): Response<AdminStorageStatusDto>

    @POST("upload-sessions")
    suspend fun createUploadSession(
        @Header("Authorization") authorization: String,
        @Header("Idempotency-Key") idempotencyKey: String,
        @Body request: CreateUploadSessionRequestDto,
    ): Response<UploadSessionDto>

    @GET("upload-sessions/{sessionId}")
    suspend fun getUploadSession(
        @Header("Authorization") authorization: String,
        @Path("sessionId") sessionId: String,
    ): Response<UploadSessionDto>

    @PUT("upload-sessions/{sessionId}/chunks")
    suspend fun uploadChunk(
        @Header("Authorization") authorization: String,
        @Path("sessionId") sessionId: String,
        @Header("Upload-Offset") offset: Long,
        @Header("X-Chunk-Sha256") sha256: String,
        @Body body: RequestBody,
    ): Response<UploadChunkDto>

    @POST("upload-sessions/{sessionId}/complete")
    suspend fun completeUploadSession(
        @Header("Authorization") authorization: String,
        @Path("sessionId") sessionId: String,
    ): Response<FileEntryDto>

    @DELETE("upload-sessions/{sessionId}")
    suspend fun cancelUploadSession(
        @Header("Authorization") authorization: String,
        @Path("sessionId") sessionId: String,
    ): Response<Unit>

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
    FileApi,
    AdminStorageApi,
    UploadSessionApi,
    SharingApi,
    SearchApi {
    private val service =
        Retrofit
            .Builder()
            .baseUrl("${baseUrl.trimEnd('/')}/")
            .client(client)
            .addConverterFactory(json.asConverterFactory(JSON_MEDIA_TYPE))
            .build()
            .create(KuraStorageService::class.java)

    suspend fun health(): SystemHealthDto = execute { service.health() }

    override suspend fun listCandidates(accessToken: String) = executeAuthenticated { shareCandidates(accessToken) }

    override suspend fun createShare(
        accessToken: String,
        request: CreateShareRequestDto,
    ) = executeAuthenticated { service.createShare(bearer(accessToken), request) }

    private suspend fun shareCandidates(accessToken: String) = service.listShareCandidates(bearer(accessToken))

    override suspend fun listShares(
        accessToken: String,
        scope: String,
        targetType: String?,
        page: Int,
        pageSize: Int,
    ) = executeAuthenticated { service.listShares(bearer(accessToken), scope, targetType, page, pageSize) }

    override suspend fun getShare(
        accessToken: String,
        shareId: String,
    ) = executeAuthenticated { service.getShare(bearer(accessToken), shareId) }

    override suspend fun setMember(
        accessToken: String,
        shareId: String,
        userId: String,
        request: SetShareMemberRequestDto,
    ) = executeAuthenticated { service.setShareMember(bearer(accessToken), shareId, userId, request) }

    override suspend fun removeMember(
        accessToken: String,
        shareId: String,
        userId: String,
    ) = executeAuthenticatedNoContent { service.removeShareMember(bearer(accessToken), shareId, userId) }

    override suspend fun deleteShare(
        accessToken: String,
        shareId: String,
    ) = executeAuthenticatedNoContent { service.deleteShare(bearer(accessToken), shareId) }

    override suspend fun search(
        accessToken: String,
        request: SearchRequestDto,
    ) = executeAuthenticated {
        service.search(
            bearer(accessToken),
            request.query,
            request.entryType,
            request.fileCategory,
            request.status,
            request.updatedFrom,
            request.updatedTo,
            request.minSize,
            request.maxSize,
            request.ownerUserId,
            request.shareTargetId,
            request.page,
            request.pageSize,
        )
    }

    override suspend fun listRecentFiles(
        accessToken: String,
        page: Int,
        pageSize: Int,
    ) = executeAuthenticated { service.listRecentFiles(bearer(accessToken), page, pageSize) }

    override suspend fun recordRecentFile(
        accessToken: String,
        fileId: String,
    ) = executeAuthenticatedNoContent { service.recordRecentFile(bearer(accessToken), fileId) }

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

    override suspend fun updateFile(
        accessToken: String,
        fileId: String,
        request: UpdateFileRequestDto,
    ) = executeAuthenticated { service.updateFile(bearer(accessToken), fileId, request) }

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

    override suspend fun recheckMissing(
        accessToken: String,
        fileId: String,
    ) = executeAuthenticated { service.recheckMissing(bearer(accessToken), fileId) }

    override suspend fun deleteMissingIndexEntry(
        accessToken: String,
        fileId: String,
    ) = executeAuthenticatedNoContent { service.deleteMissingIndexEntry(bearer(accessToken), fileId) }

    override suspend fun purge(
        accessToken: String,
        fileId: String,
        idempotencyKey: String,
    ) = executeAuthenticatedNoContent { service.purge(bearer(accessToken), idempotencyKey, fileId) }

    override suspend fun getAdminStorage(accessToken: String): NetworkCallResult<AdminStorageStatusDto> =
        executeAuthenticated {
            service.getAdminStorage(bearer(accessToken))
        }

    override suspend fun createUploadSession(
        accessToken: String,
        idempotencyKey: String,
        request: CreateUploadSessionRequestDto,
    ) = executeAuthenticated { service.createUploadSession(bearer(accessToken), idempotencyKey, request) }

    override suspend fun getUploadSession(
        accessToken: String,
        sessionId: String,
    ) = executeAuthenticated { service.getUploadSession(bearer(accessToken), sessionId) }

    override suspend fun uploadChunk(
        accessToken: String,
        sessionId: String,
        offset: Long,
        sha256: String,
        body: RequestBody,
    ) = executeAuthenticated { service.uploadChunk(bearer(accessToken), sessionId, offset, sha256, body) }

    override suspend fun completeUploadSession(
        accessToken: String,
        sessionId: String,
    ) = executeAuthenticated { service.completeUploadSession(bearer(accessToken), sessionId) }

    override suspend fun cancelUploadSession(
        accessToken: String,
        sessionId: String,
    ) = executeAuthenticatedNoContent { service.cancelUploadSession(bearer(accessToken), sessionId) }

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
                    throw apiException(response.code(), response.errorBody()?.string().orEmpty(), response.headers())
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
                    throw apiException(response.code(), response.errorBody()?.string().orEmpty(), response.headers())
                }
            } catch (error: KuraStorageException) {
                throw error
            } catch (error: IOException) {
                throw KuraStorageException.Network(error)
            } catch (error: SerializationException) {
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
                    throw apiException(response.code(), response.errorBody()?.string().orEmpty(), response.headers())
                }
                NetworkCallResult.Success(
                    response.body() ?: throw SerializationException("Successful API response had no body"),
                )
            } catch (error: KuraStorageException) {
                throw error
            } catch (error: IOException) {
                throw KuraStorageException.Network(error)
            } catch (error: SerializationException) {
                throw KuraStorageException.Network(error)
            }
        }

    private suspend fun executeAuthenticatedNoContent(call: suspend () -> Response<Unit>): NetworkCallResult<Unit> =
        withContext(Dispatchers.IO) {
            try {
                val response = call()
                if (response.code() == HTTP_UNAUTHORIZED) return@withContext NetworkCallResult.Unauthorized
                if (!response.isSuccessful) {
                    throw apiException(response.code(), response.errorBody()?.string().orEmpty(), response.headers())
                }
                NetworkCallResult.Success(Unit)
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
        headers: okhttp3.Headers = okhttp3.Headers.headersOf(),
    ): KuraStorageException.Api {
        val response = runCatching { json.decodeFromString<ErrorResponseDto>(body) }.getOrNull()
        val code = response?.code?.let { runCatching { ErrorCode.valueOf(it) }.getOrNull() } ?: ErrorCode.UNKNOWN
        return KuraStorageException.Api(
            ApiError(
                code,
                response?.requestId,
                statusCode,
                headers["Retry-After"]?.toLongOrNull(),
                headers["Upload-Offset"]?.toLongOrNull(),
            ),
        )
    }

    private companion object {
        val JSON_MEDIA_TYPE = "application/json; charset=utf-8".toMediaType()
        const val HTTP_UNAUTHORIZED = 401
    }
}
