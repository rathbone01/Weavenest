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
    var initialHeight = window.visualViewport.height;
    window.visualViewport.addEventListener('resize', function () {
        var currentHeight = window.visualViewport.height;
        var container = document.querySelector('.chat-container');
        if (container) {
            if (currentHeight < initialHeight - 50) {
                // Keyboard is open — shrink container to fit visible area
                container.style.height = currentHeight + 'px';
            } else {
                // Keyboard is closed — remove inline style so CSS rule takes over
                container.style.height = '';
            }
        }
        window.scrollTo(0, 0);
    });
}
