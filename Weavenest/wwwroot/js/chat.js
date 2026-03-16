window.chatInterop = {
    scrollToBottom: function (element) {
        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    },
    highlightCode: function () {
        // Syntax highlighting hook — activate by adding highlight.js to App.razor
        if (window.hljs) {
            document.querySelectorAll('pre code').forEach(function (block) {
                hljs.highlightElement(block);
            });
        }
    },
    registerEnterSend: function (container, dotnetRef) {
        var textarea = container.querySelector('textarea');
        if (!textarea) return;
        textarea.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                // Pass the raw textarea value directly — Blazor's bind:onchange hasn't fired yet
                dotnetRef.invokeMethodAsync('SendMessageFromJs', textarea.value);
                textarea.value = '';
            }
        });
    }
};
