// 虚拟化数据备份还原系统 —— 控制台脚本
// 说明：仅提供少量无依赖的交互增强，不使用任何外部库，确保离线环境可用。

// 还原页：全选 / 反选文件清单
function toggleAllFiles(checked) {
    document.querySelectorAll('input[name="SelectedFiles"]').forEach(function (box) {
        box.checked = checked;
    });
    updateSelectedCount();
}

// 统计当前勾选的文件数并刷新提示
function updateSelectedCount() {
    var boxes = document.querySelectorAll('input[name="SelectedFiles"]');
    var count = 0;
    boxes.forEach(function (box) {
        if (box.checked) {
            count++;
        }
    });

    var target = document.getElementById('selectedCount');
    if (target) {
        target.textContent = count;
    }
}

document.addEventListener('DOMContentLoaded', function () {
    // 单个复选框变化时同步计数
    document.querySelectorAll('input[name="SelectedFiles"]').forEach(function (box) {
        box.addEventListener('change', updateSelectedCount);
    });

    updateSelectedCount();
});
