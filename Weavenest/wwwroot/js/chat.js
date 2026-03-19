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

// Mobile keyboard handling: prevent page scroll, keep input visible
if (window.visualViewport && window.innerWidth <= 768) {
    var lastHeight = window.visualViewport.height;
    window.visualViewport.addEventListener('resize', function () {
        var currentHeight = window.visualViewport.height;
        var container = document.querySelector('.chat-container');
        if (container) {
            // Resize the chat container to match the visible area
            container.style.height = window.visualViewport.height + 'px';
        }
        // Prevent the browser from scrolling the page
        window.scrollTo(0, 0);
        lastHeight = currentHeight;
    });
}
