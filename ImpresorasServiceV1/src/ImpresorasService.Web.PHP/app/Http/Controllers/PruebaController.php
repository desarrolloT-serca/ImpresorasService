<?php

namespace App\Http\Controllers;

use App\Helpers\AuthHelper;
use App\Services\ApiClient;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\View\View;

class PruebaController extends Controller
{
    public function __construct(private readonly ApiClient $api)
    {
    }

    public function index(): View
    {
        $effectiveStore = AuthHelper::getEffectiveStoreId();
        return view('prueba', ['defaultStoreId' => $effectiveStore ?? 101]);
    }

    public function store(Request $request): RedirectResponse
    {
        $request->validate([
            'storeId' => 'required|integer|min:1',
            'sourceSystem' => 'nullable|string|max:255',
            'externalJobId' => 'nullable|string|max:255',
            'documentType' => 'nullable|string|max:255',
            'channel' => 'nullable|string|max:255',
            'pdf' => 'nullable|file|mimes:pdf|max:10240',
        ]);

        try {
            $body = array_filter([
                'storeId' => $request->integer('storeId'),
                'sourceSystem' => $request->input('sourceSystem') ?: 'SAP-TEST',
                'externalJobId' => $request->filled('externalJobId') ? $request->input('externalJobId') : null,
                'documentType' => $request->input('documentType') ?: 'TICKET',
                'channel' => $request->input('channel') ?: 'DEFAULT',
            ], fn ($v) => $v !== null);

            if ($request->hasFile('pdf')) {
                $body['pdfBlob'] = base64_encode($request->file('pdf')->get());
            }

            $data = $this->api->post('api/sourceprintjobs/test', $body);
            return back()->with('success', 'Trabajo de prueba creado: ' . ($data['externalJobId'] ?? $data['ExternalJobId'] ?? 'OK'));
        } catch (\GuzzleHttp\Exception\RequestException $e) {
            return back()->withInput()->withErrors($this->extractApiErrors($e, 'form'));
        }
    }
}
