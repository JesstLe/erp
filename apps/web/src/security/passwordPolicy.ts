import type { Rule } from 'antd/es/form'

export const PASSWORD_MIN_LENGTH = 8
export const PASSWORD_MAX_LENGTH = 256
export const PASSWORD_POLICY_HINT = '至少8位，且同时包含英文字母和数字；特殊字符可选'

export function isPasswordPolicyCompliant(password: string): boolean {
  return password.length >= PASSWORD_MIN_LENGTH &&
    password.length <= PASSWORD_MAX_LENGTH &&
    /[A-Za-z]/.test(password) &&
    /\d/.test(password)
}

export function passwordRules(requiredMessage = '请输入密码'): Rule[] {
  return [
    { required: true, message: requiredMessage },
    { min: PASSWORD_MIN_LENGTH, message: `密码至少${PASSWORD_MIN_LENGTH}位` },
    { max: PASSWORD_MAX_LENGTH, message: `密码不能超过${PASSWORD_MAX_LENGTH}位` },
    { pattern: /[A-Za-z]/, message: '密码必须至少包含一个英文字母' },
    { pattern: /\d/, message: '密码必须至少包含一个数字' },
  ]
}
