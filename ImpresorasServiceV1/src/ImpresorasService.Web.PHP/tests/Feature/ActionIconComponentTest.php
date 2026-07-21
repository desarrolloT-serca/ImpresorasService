<?php

namespace Tests\Feature;

use Tests\TestCase;

class ActionIconComponentTest extends TestCase
{
    public function test_action_icon_renders_accessible_icon_without_visible_text(): void
    {
        $view = $this->blade('<x-ui.action-icon name="edit" label="Editar usuario" />');

        $view->assertSee('aria-label="Editar usuario"', false);
        $view->assertSee('title="Editar usuario"', false);
        $view->assertSee('aria-hidden="true"', false);
        $view->assertDontSee('>Editar usuario<', false);
    }
}
