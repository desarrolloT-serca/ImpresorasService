<?php

namespace App\Http\Controllers;

use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;

class StoreFilterController extends Controller
{
    public function update(Request $request): RedirectResponse
    {
        $storeId = $request->input('storeId');
        if ($storeId === '' || $storeId === null) {
            session()->forget('selected_store_id');
        } else {
            session()->put('selected_store_id', (int) $storeId);
        }
        return back();
    }
}
