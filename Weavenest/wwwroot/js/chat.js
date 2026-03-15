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
    }
};
