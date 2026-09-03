<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useFileStore } from '../stores/files'
import { useSealStore } from '../stores/seal'
import { useUiStore } from '../stores/ui'
import { businessStatusLabel } from '../utils/businessLabels'

const app = useAppStore()
const auth = useAuthStore()
const employees = useEmployeeDirectoryStore()
const files = useFileStore()
const seal = useSealStore()
const ui = useUiStore()
const router = useRouter()

const documentCategories = ['合同协议', '招投标文件', '证照资质', '财务票据', '人事证明', '其他']
const sealTypes = ['公章', '法人章', '合同章', '财务章', '电子印章']

async function selectAttachments(event: Event) {
  const selected = Array.from((event.target as HTMLInputElement).files ?? [])
  try {
    seal.sealForm.attachments.push(...await Promise.all(selected.map(file => files.uploadFile(file))))
  } catch (cause) {
    ui.error = cause instanceof Error ? cause.message : '附件上传失败。'
  }
}

function search() {
  seal.page = 1
  void seal.loadSeals()
}

function changePage(offset: number) {
  seal.page += offset
  void seal.loadSeals()
}

function resetFilters() {
  Object.assign(seal.filters, { keyword: '', status: '', applicantId: '', startDate: '', endDate: '' })
  search()
}
</script>

<template>
  <div class="page-heading">
    <div>
      <p class="eyebrow">SEAL MANAGEMENT</p>
      <h1>用章管理</h1>
      <p>发起用章申请，按风险分级审批，并跟踪在司用印或外带借出归还。</p>
    </div>
    <div class="task-actions">
      <button
        v-if="auth.currentUser?.permissions?.includes('SEAL_MANAGE')"
        class="secondary"
        :disabled="seal.operationBusy"
        @click="seal.generateDemoData"
      >
        生成演示草稿
      </button>
      <button class="primary-action" @click="seal.showSealForm = true">＋ 新建用章</button>
    </div>
  </div>

  <p v-if="ui.message" class="notice success">{{ ui.message }}</p>
  <p v-if="ui.error" class="notice error">{{ ui.error }}</p>

  <form class="filter-bar" @submit.prevent="search">
    <input v-model="seal.filters.keyword" placeholder="单号、主题、文件名称或申请人">
    <select v-model="seal.filters.status">
      <option value="">全部状态</option>
      <option value="0">草稿</option>
      <option value="1">审批中</option>
      <option value="2">已驳回</option>
      <option value="3">已批准</option>
      <option value="4">已撤回</option>
      <option value="5">已用印</option>
      <option value="6">外带借出</option>
      <option value="7">已归还</option>
    </select>
    <select v-model="seal.filters.applicantId">
      <option value="">全部申请人</option>
      <option v-for="employee in employees.employees" :key="employee.id" :value="employee.id">
        {{ employee.name }}
      </option>
    </select>
    <button type="submit">查询</button>
    <button class="secondary" type="button" @click="resetFilters">重置</button>
  </form>

  <section class="panel">
    <div class="section-title">
      <div>
        <p class="eyebrow">SEAL REQUESTS</p>
        <h2>用章申请</h2>
      </div>
      <button @click="seal.showSealForm = !seal.showSealForm">
        {{ seal.showSealForm ? '收起表单' : '新建用章' }}
      </button>
    </div>

    <form v-if="seal.showSealForm" class="leave-form" @submit.prevent="app.submitSeal">
      <label>
        用印主题
        <input v-model="seal.sealForm.title" maxlength="100" placeholder="例如 采购框架协议盖章申请">
      </label>
      <label>
        文件类别
        <select v-model="seal.sealForm.documentCategory">
          <option v-for="category in documentCategories" :key="category" :value="category">{{ category }}</option>
        </select>
      </label>
      <label>
        文件名称
        <input v-model="seal.sealForm.documentName" maxlength="100" placeholder="例如 2026年度办公设备供货合同">
      </label>
      <label>
        印章类型
        <select v-model="seal.sealForm.sealType">
          <option v-for="st in sealTypes" :key="st" :value="st">{{ st }}</option>
        </select>
      </label>
      <label>
        用印份数
        <input v-model="seal.sealForm.copies" type="number" min="1" max="100">
      </label>
      <label class="checkbox-field">
        <input v-model="seal.sealForm.isOut" type="checkbox">
        外带借出印章
      </label>

      <template v-if="seal.sealForm.isOut">
        <label>
          预计借出日期
          <input v-model="seal.sealForm.outStartDate" type="date">
        </label>
        <label>
          预计归还日期
          <input v-model="seal.sealForm.outEndDate" type="date">
        </label>
        <label>
          外带保管人
          <input v-model="seal.sealForm.outCustodian" maxlength="64" placeholder="保管人姓名">
        </label>
      </template>

      <label class="wide">
        用印事由
        <textarea v-model="seal.sealForm.reason" maxlength="500" placeholder="请说明用印用途、背景与必要性"></textarea>
      </label>

      <fieldset class="wide copy-selector">
        <legend>抄送人（审批完成或撤回后通知）</legend>
        <label v-for="employee in employees.employees.filter(item => item.id !== auth.currentUserId)" :key="employee.id">
          <input v-model="seal.sealForm.copyRecipientIds" type="checkbox" :value="employee.id">
          {{ employee.name }} · {{ employee.role }}
        </label>
      </fieldset>

      <label class="wide">
        用印文件及附件
        <input accept=".pdf,.jpg,.jpeg,.png,.doc,.docx" multiple type="file" @change="selectAttachments">
        <small>支持上传待盖章文件、合同底稿或批准依据；单个文件不超过 20MB。</small>
      </label>
      <div v-if="seal.sealForm.attachments.length" class="wide attachment-list">
        已上传 {{ seal.sealForm.attachments.length }} 个附件
        <button class="secondary" type="button" @click="seal.sealForm.attachments = []">清空</button>
      </div>

      <div class="wide form-actions">
        <button :disabled="seal.submitting" type="submit">
          {{ seal.submitting ? '提交中…' : '保存并提交' }}
        </button>
      </div>
    </form>

    <template v-if="seal.paged.items.length">
      <div class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th>申请单号</th>
              <th>主题</th>
              <th>申请人</th>
              <th>印章类型</th>
              <th>文件类别 / 名称</th>
              <th>份数</th>
              <th>使用方式</th>
              <th>状态</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in seal.paged.items" :key="item.id">
              <td>{{ item.number }}</td>
              <td>{{ item.title }}</td>
              <td>
                {{ item.applicantName }}
                <small>{{ item.departmentName }}</small>
              </td>
              <td>{{ item.sealType }}</td>
              <td>{{ item.documentCategory }} · {{ item.documentName }}</td>
              <td>{{ item.copies }} 份</td>
              <td>{{ item.isOut ? '外带借出' : '在司用印' }}</td>
              <td><em>{{ businessStatusLabel(item.status) }}</em></td>
              <td>
                <button class="secondary" @click="router.push(`/seal/${item.id}`)">详情</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="pagination">
        <span>共 {{ seal.paged.total }} 条</span>
        <div>
          <button class="secondary" :disabled="seal.paged.currentPage === 1" @click="changePage(-1)">上一页</button>
          <b>{{ seal.paged.currentPage }} / {{ seal.paged.totalPages }}</b>
          <button class="secondary" :disabled="seal.paged.currentPage === seal.paged.totalPages" @click="changePage(1)">下一页</button>
        </div>
      </div>
    </template>
    <p v-else class="empty">当前筛选条件下暂无用章记录。</p>
  </section>
</template>
