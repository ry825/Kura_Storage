package com.kurastorage.feature.auth

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.AuthenticationRepository
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.DeviceRegistrationMetadata
import com.kurastorage.core.model.ErrorCode
import com.kurastorage.core.model.KuraStorageException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

sealed interface AuthUiState {
    data object Loading : AuthUiState

    data class Form(
        val registration: Boolean,
        val username: String = "",
        val deviceName: String = "",
        val submitting: Boolean = false,
        val error: ApiError? = null,
    ) : AuthUiState

    data object RequiresLocalDirect : AuthUiState

    data object Authenticated : AuthUiState

    data class Error(
        val error: ApiError,
    ) : AuthUiState
}

class AuthViewModel(
    private val route: ConnectionRoute,
    private val deviceName: String,
    private val repository: AuthenticationRepository,
) : ViewModel() {
    private val mutableState = MutableStateFlow<AuthUiState>(AuthUiState.Loading)
    val state: StateFlow<AuthUiState> = mutableState.asStateFlow()

    init {
        load()
    }

    fun load() {
        mutableState.value = AuthUiState.Loading
        viewModelScope.launch {
            val registration = repository.storedRegistration()
            if (registration == null) {
                mutableState.value = registrationState()
                return@launch
            }
            val credential = repository.storedCredential()
            if (credential == null) {
                mutableState.value = signInState(registration)
                return@launch
            }
            mutableState.value =
                try {
                    repository.refresh()
                    AuthUiState.Authenticated
                } catch (error: KuraStorageException.Api) {
                    refreshFailure(registration, error.error)
                } catch (_: KuraStorageException) {
                    AuthUiState.Error(unknownError())
                }
        }
    }

    fun submit(
        username: String,
        password: String,
    ) {
        val form = mutableState.value as? AuthUiState.Form ?: return
        if (form.submitting) return
        mutableState.value = form.copy(username = username, submitting = true, error = null)
        viewModelScope.launch {
            try {
                if (form.registration) {
                    repository.register(route, username, password, deviceName)
                } else {
                    repository.login(username, password)
                }
                mutableState.value = AuthUiState.Authenticated
            } catch (error: KuraStorageException.Api) {
                mutableState.value = submitFailure(form, username, error.error)
            } catch (_: KuraStorageException) {
                mutableState.value = form.copy(username = username, submitting = false, error = unknownError())
            }
        }
    }

    fun logout(onComplete: () -> Unit) {
        viewModelScope.launch {
            runCatching { repository.logout() }
            onComplete()
        }
    }

    private fun registrationState(): AuthUiState =
        if (route == ConnectionRoute.LOCAL_DIRECT) {
            AuthUiState.Form(registration = true, deviceName = deviceName)
        } else {
            AuthUiState.RequiresLocalDirect
        }

    private fun signInState(
        registration: DeviceRegistrationMetadata,
        error: ApiError? = null,
    ): AuthUiState.Form =
        AuthUiState.Form(
            registration = false,
            username = registration.username.orEmpty(),
            error = error,
        )

    private fun refreshFailure(
        registration: DeviceRegistrationMetadata,
        error: ApiError,
    ): AuthUiState =
        when (error.code) {
            ErrorCode.DEVICE_REVOKED -> registrationState()
            ErrorCode.AUTHENTICATION_REQUIRED,
            ErrorCode.REFRESH_TOKEN_REUSED,
            -> signInState(registration, error)
            else -> AuthUiState.Error(error)
        }

    private fun submitFailure(
        form: AuthUiState.Form,
        username: String,
        error: ApiError,
    ): AuthUiState =
        when (error.code) {
            ErrorCode.DEVICE_REGISTRATION_REQUIRES_LOCAL_DIRECT -> AuthUiState.RequiresLocalDirect
            ErrorCode.DEVICE_REVOKED -> registrationState()
            else -> form.copy(username = username, submitting = false, error = error)
        }

    private fun unknownError() = ApiError(ErrorCode.UNKNOWN, requestId = null, statusCode = null)
}
