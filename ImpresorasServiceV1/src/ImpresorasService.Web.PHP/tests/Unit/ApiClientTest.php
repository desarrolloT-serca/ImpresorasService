<?php

namespace Tests\Unit;

use App\Services\ApiClient;
use GuzzleHttp\Client;
use GuzzleHttp\Handler\MockHandler;
use GuzzleHttp\HandlerStack;
use GuzzleHttp\Psr7\Response;
use Illuminate\Support\Facades\Cache;
use Tests\TestCase;

class ApiClientTest extends TestCase
{
    public function test_post_clears_in_memory_get_cache(): void
    {
        $mock = new MockHandler([
            new Response(200, [], json_encode(['items' => [1]])),
            new Response(200, [], json_encode(['ok' => true])),
            new Response(200, [], json_encode(['items' => [2]])),
        ]);
        $client = new Client(['handler' => HandlerStack::create($mock), 'base_uri' => 'http://api.test/']);
        $api = new ApiClient($client, 'http://api.test');

        $first = $api->get('api/stores');
        $this->assertSame([1], $first['items']);

        $api->post('api/stores', ['name' => 'Nueva']);

        $second = $api->get('api/stores');
        $this->assertSame([2], $second['items']);
    }

    public function test_post_forgets_layout_store_cache(): void
    {
        Cache::store('file')->put('layout_stores_active', ['cached' => true], 60);

        $mock = new MockHandler([
            new Response(200, [], json_encode(['ok' => true])),
        ]);
        $client = new Client(['handler' => HandlerStack::create($mock), 'base_uri' => 'http://api.test/']);
        $api = new ApiClient($client, 'http://api.test');

        $api->post('api/stores', ['name' => 'Nueva']);

        $this->assertNull(Cache::store('file')->get('layout_stores_active'));
    }
}
