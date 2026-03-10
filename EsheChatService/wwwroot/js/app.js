window.chatScroll = {
    scrollToBottom: el => {
        if (!el) return;
        el.scrollTop = el.scrollHeight;
    },
    isNearBottom: el => {
        if (!el) return true;
        return el.scrollHeight - el.scrollTop - el.clientHeight < 150;
    },
    initAutoResize: el => {
        if (!el) return;
        const resize = () => {
            el.style.height = 'auto';
            let newHeight = el.scrollHeight;
            if (newHeight > 200) {
                el.style.height = '200px';
                el.style.overflowY = 'auto';
            } else {
                el.style.height = newHeight + 'px';
                el.style.overflowY = 'hidden';
            }
        };
        el.addEventListener('input', resize);
        el.addEventListener('keydown', e => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
            }
        });
        resize();
    },
    resetAutoResize: el => {
        if (!el) return;
        el.style.height = 'auto';
        el.style.overflowY = 'hidden';
    },
    copyText: async (text) => {
        try {
            await navigator.clipboard.writeText(text);
        } catch (err) {
            console.error('Failed to copy: ', err);
        }
    },
    formatCodeBlocks: () => {
        const blocks = document.querySelectorAll('.message-content pre code');
        blocks.forEach(block => {
            if (block.parentElement.parentElement && block.parentElement.parentElement.classList.contains('code-block-wrapper')) return;

            const pre = block.parentElement;
            const wrapper = document.createElement('div');
            wrapper.className = 'code-block-wrapper';
            
            const header = document.createElement('div');
            header.className = 'code-block-header';
            
            let langText = 'code';
            block.classList.forEach(cls => {
                if (cls.startsWith('language-')) {
                    langText = cls.replace('language-', '');
                }
            });
            
            const langSpan = document.createElement('span');
            langSpan.className = 'code-block-lang';
            langSpan.innerText = langText;
            
            const copyBtn = document.createElement('button');
            copyBtn.className = 'code-block-copy';
            copyBtn.innerText = 'Copy code';
            copyBtn.onclick = async () => {
                try {
                    await navigator.clipboard.writeText(block.innerText);
                    copyBtn.innerText = 'Copied!';
                    setTimeout(() => copyBtn.innerText = 'Copy code', 2000);
                } catch {
                    copyBtn.innerText = 'Error';
                }
            };
            
            header.appendChild(langSpan);
            header.appendChild(copyBtn);
            
            pre.parentNode.insertBefore(wrapper, pre);
            wrapper.appendChild(header);
            wrapper.appendChild(pre);
        });
    }
};