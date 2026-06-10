@if($errors->any())
    <div class="mb-4 alert alert-error">
        <ul class="list-disc pl-5 space-y-1">
            @foreach($errors->all() as $error)
                <li>{{ $error }}</li>
            @endforeach
        </ul>
    </div>
@endif

@if(session('error'))
    <div class="mb-4 alert alert-error">{{ session('error') }}</div>
@endif
