@props(['colspan' => 1, 'message' => 'Sin resultados.'])

<tr>
    <td colspan="{{ $colspan }}" class="dbx-empty">{{ $message }}</td>
</tr>
