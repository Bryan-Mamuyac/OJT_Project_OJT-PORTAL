// ITPMS OJT System – Main JavaScript

document.addEventListener('DOMContentLoaded', function () {

    // ===== SIDEBAR TOGGLE (Desktop collapse) =====
    const sidebar = document.getElementById('sidebar');
    const sidebarToggle = document.getElementById('sidebarToggle');
    const mobileToggle = document.getElementById('mobileToggle');

    if (sidebarToggle) {
        sidebarToggle.addEventListener('click', function () {
            if (window.innerWidth > 768) {
                const collapsed = sidebar.classList.toggle('collapsed');
                localStorage.setItem('sidebarCollapsed', collapsed);
            }
        });
    }

    // ===== MOBILE SIDEBAR OPEN/CLOSE =====
    if (mobileToggle) {
        mobileToggle.addEventListener('click', function () {
            sidebar.classList.toggle('open');
        });
    }

    // Close sidebar when clicking outside (mobile)
    document.addEventListener('click', function (e) {
        if (window.innerWidth <= 768 && sidebar && mobileToggle) {
            if (!sidebar.contains(e.target) && !mobileToggle.contains(e.target)) {
                sidebar.classList.remove('open');
            }
        }
    });

    // Restore sidebar collapsed state on load
    if (localStorage.getItem('sidebarCollapsed') === 'true' && sidebar && window.innerWidth > 768) {
        sidebar.classList.add('collapsed');
    }

    // ===== AUTO-DISMISS ALERTS after 5s =====
    document.querySelectorAll('.alert').forEach(function (el) {
        setTimeout(function () {
            el.style.transition = 'opacity .5s, transform .5s';
            el.style.opacity = '0';
            el.style.transform = 'translateY(-8px)';
            setTimeout(() => el.remove(), 500);
        }, 5000);
    });
});

// ===== SIDEBAR COLLAPSED STYLES (injected) =====
const s = document.createElement('style');
s.textContent = `
  /* ── Collapsed width ── */
  .sidebar.collapsed { width: 68px !important; overflow: hidden; }
  .sidebar.collapsed ~ .main-content { margin-left: 68px !important; }

  /* ── Hide text elements ── */
  .sidebar.collapsed .brand-logo span,
  .sidebar.collapsed .user-info,
  .sidebar.collapsed .logout-btn span,
  .sidebar.collapsed .nav-item > span:not(.nav-badge) { display: none !important; }

  /* ── Keep badges visible when collapsed — reposition as corner dot ── */
  .sidebar.collapsed .nav-badge {
    display: flex !important;
    position: absolute !important;
    top: 4px !important;
    right: 8px !important;
    min-width: 18px !important;
    height: 18px !important;
    font-size: .65rem !important;
    padding: 0 4px !important;
    border-radius: 9px !important;
    align-items: center !important;
    justify-content: center !important;
    line-height: 1 !important;
    z-index: 2 !important;
  }

  /* ── Center the nav items ── */
  .sidebar.collapsed .nav-item {
    justify-content: center !important;
    padding: 12px 0 !important;
    position: relative;
  }
  .sidebar.collapsed .nav-item i { margin: 0 !important; }

  /* ── Center sidebar header ── */
  .sidebar.collapsed .sidebar-header {
    justify-content: center !important;
    padding: 0 !important;
  }
  .sidebar.collapsed .brand-logo { display: none !important; }
  .sidebar.collapsed .sidebar-toggle { margin: 0 auto !important; }

  /* ── Center user avatar ── */
  .sidebar.collapsed .sidebar-user {
    justify-content: center !important;
    padding: 12px 0 !important;
  }
  .sidebar.collapsed .user-avatar {
    margin: 0 auto !important;
    flex-shrink: 0;
  }

  /* ── Footer: hide dept badge, show only logout icon ── */
  .sidebar.collapsed .sidebar-footer {
    justify-content: center !important;
    flex-direction: column !important;
    align-items: center !important;
    padding: 12px 0 !important;
    gap: 0 !important;
  }
  .sidebar.collapsed .dept-badge { display: none !important; }
  .sidebar.collapsed .logout-btn {
    display: flex !important;
    justify-content: center !important;
    align-items: center !important;
    width: 44px !important;
    height: 44px !important;
    padding: 0 !important;
    border-radius: 10px !important;
    margin: 0 auto !important;
    background: rgba(255,255,255,.07) !important;
    border: 1px solid rgba(255,255,255,.12) !important;
  }
  .sidebar.collapsed .logout-btn:hover {
    background: rgba(220,38,38,.18) !important;
    border-color: rgba(220,38,38,.3) !important;
  }
  .sidebar.collapsed .logout-btn i {
    font-size: 1rem !important;
    color: #94a3b8 !important;
    margin: 0 !important;
  }
  .sidebar.collapsed .logout-btn:hover i { color: #fca5a5 !important; }

  /* ── Tooltip on hover when collapsed ── */
  .sidebar.collapsed .nav-item::after {
    content: attr(data-tooltip);
    position: fixed;
    left: 76px;
    top: auto;
    margin-top: -14px;
    background: #1a3a5c;
    color: #fff;
    padding: 5px 10px;
    border-radius: 6px;
    font-size: .78rem;
    font-weight: 600;
    white-space: nowrap;
    opacity: 0;
    pointer-events: none;
    transition: opacity .15s;
    z-index: 9999;
    box-shadow: 0 2px 8px rgba(0,0,0,.25);
  }
  .sidebar.collapsed .nav-item:hover::after { opacity: 1; }

  @keyframes slideInDown {
    from { opacity: 0; transform: translateY(-12px); }
    to   { opacity: 1; transform: translateY(0); }
  }
  .alert { animation: slideInDown .3s ease; }
`;
document.head.appendChild(s);