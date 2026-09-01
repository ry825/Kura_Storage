package com.kurastorage.app

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.createSavedStateHandle
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory

inline fun <reified T : ViewModel> simpleViewModelFactory(crossinline create: () -> T): ViewModelProvider.Factory =
    object : ViewModelProvider.Factory {
        override fun <ModelType : ViewModel> create(modelClass: Class<ModelType>): ModelType {
            require(modelClass.isAssignableFrom(T::class.java))
            @Suppress("UNCHECKED_CAST")
            return create() as ModelType
        }
    }

@Suppress("MaxLineLength")
inline fun <reified T : ViewModel> savedStateViewModelFactory(crossinline create: (SavedStateHandle) -> T): ViewModelProvider.Factory =
    viewModelFactory {
        initializer { create(createSavedStateHandle()) }
    }
