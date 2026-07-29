package com.kurastorage.feature.auth

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.kurastorage.core.data.AuthenticationRepository
import com.kurastorage.core.model.ApiError
import com.kurastorage.core.model.ConnectionRoute
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
    ) : AuthUiState

    data object RequiresLocalDirect : AuthUiState

    data object Submitting : AuthUiState

    data object Authenticated : AuthUiState

    data class Error(
        val error: ApiError,
        val registration: Boolean,
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
            val credential = repository.storedCredential()
            mutableState.value =
                when {
                    credential != null -> AuthUiState.Form(false, credential.username.orEmpty())
                    route == ConnectionRoute.LOCAL_DIRECT -> AuthUiState.Form(true)
                    else -> AuthUiState.RequiresLocalDirect
                }
        }
    }

    fun submit(
        username: String,
        password: String,
    ) {
        val form = mutableState.value as? AuthUiState.Form ?: return
        mutableState.value = AuthUiState.Submitting
        viewModelScope.launch {
            try {
                if (form.registration) {
                    repository.register(route, username, password, deviceName)
                } else {
                    repository.login(username, password)
                }
                mutableState.value = AuthUiState.Authenticated
            } catch (error: KuraStorageException.Api) {
                mutableState.value = AuthUiState.Error(error.error, form.registration)
            } catch (_: KuraStorageException) {
                mutableState.value =
                    AuthUiState.Error(
                        ApiError(
                            code = com.kurastorage.core.model.ErrorCode.UNKNOWN,
                            requestId = null,
                            statusCode = null,
                        ),
                        form.registration,
                    )
            }
        }
    }

    fun logout(onComplete: () -> Unit) {
        viewModelScope.launch {
            runCatching { repository.logout() }
            onComplete()
        }
    }
}
