<?php

use App\Http\Controllers\AuthController;
use App\Http\Controllers\AlertasController;
use App\Http\Controllers\ColaController;
use App\Http\Controllers\DashboardController;
use App\Http\Controllers\ImpresorasController;
use App\Http\Controllers\ReglasController;
use App\Http\Controllers\TiendasController;
use App\Http\Controllers\UsuariosController;
use App\Http\Controllers\PruebasController;
use App\Http\Controllers\StoreFilterController;
use Illuminate\Support\Facades\Route;

Route::get('/login', [AuthController::class, 'showLogin'])->name('login');
Route::post('/login', [AuthController::class, 'login']);
Route::post('/logout', [AuthController::class, 'logout'])->name('logout');

Route::middleware('auth.impresoras')->group(function () {
    Route::post('/store-filter', [StoreFilterController::class, 'update'])->name('store-filter.update');
    Route::get('/', [DashboardController::class, 'index'])->name('dashboard');
    Route::get('/cola', [ColaController::class, 'index']);
    Route::middleware('manager.only')->group(function () {
        Route::post('/cola/{id}/reintentar', [ColaController::class, 'reintentar'])->name('cola.reintentar');
        Route::post('/cola/{id}/cancelar', [ColaController::class, 'cancelar'])->name('cola.cancelar');
        Route::post('/cola/reintentar-masivo', [ColaController::class, 'reintentarMasivo'])->name('cola.reintentar_masivo');
        Route::post('/cola/cancelar-masivo', [ColaController::class, 'cancelarMasivo'])->name('cola.cancelar_masivo');
        Route::get('/impresoras', [ImpresorasController::class, 'index'])->name('impresoras.index');
        Route::post('/impresoras/{impresora}/netconnection', [ImpresorasController::class, 'netconnection'])->name('impresoras.netconnection');
    });
    Route::middleware('admin.only')->group(function () {
        Route::get('/impresoras/create', [ImpresorasController::class, 'create'])->name('impresoras.create');
        Route::post('/impresoras', [ImpresorasController::class, 'store'])->name('impresoras.store');
        Route::get('/impresoras/{impresora}/edit', [ImpresorasController::class, 'edit'])->name('impresoras.edit');
        Route::put('/impresoras/{impresora}', [ImpresorasController::class, 'update'])->name('impresoras.update');
        Route::delete('/impresoras/{impresora}', [ImpresorasController::class, 'destroy'])->name('impresoras.destroy');
    });
    Route::middleware('admin.only')->group(function () {
        Route::get('/ajustes', [DashboardController::class, 'ajustes'])->name('ajustes.index');
        Route::post('/ajustes/thresholds', [DashboardController::class, 'updateThresholds'])->name('ajustes.thresholds.update');
        Route::get('/reglas', [ReglasController::class, 'index'])->name('reglas.index');
        Route::get('/reglas/create', [ReglasController::class, 'create'])->name('reglas.create');
        Route::post('/reglas', [ReglasController::class, 'store'])->name('reglas.store');
        Route::get('/reglas/{regla}/edit', [ReglasController::class, 'edit'])->name('reglas.edit');
        Route::get('/reglas/printers', [ReglasController::class, 'printersByStore'])->name('reglas.printersByStore');
        Route::put('/reglas/{regla}', [ReglasController::class, 'update'])->name('reglas.update');
        Route::delete('/reglas/{regla}', [ReglasController::class, 'destroy'])->name('reglas.destroy');
        Route::get('/tiendas', [TiendasController::class, 'index'])->name('tiendas.index');
        Route::get('/tiendas/create', [TiendasController::class, 'create'])->name('tiendas.create');
        Route::post('/tiendas', [TiendasController::class, 'store'])->name('tiendas.store');
        Route::get('/tiendas/{tienda}/edit', [TiendasController::class, 'edit'])->name('tiendas.edit');
        Route::put('/tiendas/{tienda}', [TiendasController::class, 'update'])->name('tiendas.update');
        Route::delete('/tiendas/{tienda}', [TiendasController::class, 'destroy'])->name('tiendas.destroy');
        Route::post('/tiendas/{tienda}/activate', [TiendasController::class, 'activate'])->name('tiendas.activate');

        Route::get('/usuarios', [UsuariosController::class, 'index'])->name('usuarios.index');
        Route::get('/usuarios/create', [UsuariosController::class, 'create'])->name('usuarios.create');
        Route::post('/usuarios', [UsuariosController::class, 'store'])->name('usuarios.store');
        Route::get('/usuarios/{usuario}/edit', [UsuariosController::class, 'edit'])->name('usuarios.edit');
        Route::put('/usuarios/{usuario}', [UsuariosController::class, 'update'])->name('usuarios.update');
        Route::delete('/usuarios/{usuario}', [UsuariosController::class, 'destroy'])->name('usuarios.destroy');
    });
    Route::middleware('admin.only')->group(function () {
        Route::get('/alertas/configuracion', [AlertasController::class, 'configuracion'])->name('alertas.configuracion');
        Route::post('/alertas/configuracion', [AlertasController::class, 'saveConfig'])->name('alertas.config.save');
        Route::post('/alertas/chats', [AlertasController::class, 'addChat'])->name('alertas.chats.add');
        Route::post('/alertas/chats/{chatId}/delete', [AlertasController::class, 'deleteChat'])->name('alertas.chats.delete');
        Route::post('/alertas/test', [AlertasController::class, 'sendTest'])->name('alertas.test');
    });
    Route::middleware('admin.only')->group(function () {
        Route::get('/pruebas', [PruebasController::class, 'index'])->name('pruebas.index');
        Route::post('/pruebas/scenarios', [PruebasController::class, 'saveScenario'])->name('pruebas.scenarios.save');
        Route::delete('/pruebas/scenarios/{id}', [PruebasController::class, 'deleteScenario'])->name('pruebas.scenarios.delete');
        Route::post('/pruebas/pdfs', [PruebasController::class, 'uploadPdf'])->name('pruebas.pdfs.upload');
        Route::delete('/pruebas/pdfs/{id}', [PruebasController::class, 'deletePdf'])->name('pruebas.pdfs.delete');
        Route::post('/pruebas/run', [PruebasController::class, 'run'])->name('pruebas.run');
        Route::get('/pruebas/jobs/status', [PruebasController::class, 'jobsStatus'])->name('pruebas.jobs.status');
        Route::post('/pruebas/cancel', [PruebasController::class, 'cancelAll'])->name('pruebas.cancel');
    });
});
