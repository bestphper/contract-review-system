import { useState } from 'react';
import { Button, Form, Input, Modal, Space, message } from 'antd';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './api';

export function WorkflowActions({id}:{id:number}){
  const qc=useQueryClient();const [busy,setBusy]=useState(false);const [selected,setSelected]=useState<{action:string;label:string}>();const [form]=Form.useForm();
  const {data:actions=[]}=useQuery({queryKey:['contract-actions',id],queryFn:()=>api.request<{action:string;label:string}[]>(`/api/contracts/${id}/actions`)});
  const execute=async(action:string,opinion?:string)=>{setBusy(true);try{await api.workflow(id,action,opinion);message.success('流程已更新');setSelected(undefined);form.resetFields();for(const key of ['contracts','contract','contract-actions','workflow','notifications','dashboard','reports','audit-logs'])await qc.invalidateQueries({queryKey:[key]})}catch(e){message.error((e as Error).message)}finally{setBusy(false)}};
  return <><Space wrap>{actions.filter(a=>a.action!=='void').map(a=><Button key={a.action} loading={busy} disabled={busy} onClick={()=>setSelected(a)}>{a.label}</Button>)}</Space><Modal title={selected?.label} open={!!selected} confirmLoading={busy} cancelButtonProps={{disabled:busy}} onCancel={()=>{if(!busy)setSelected(undefined)}} onOk={()=>form.submit()}><Form form={form} layout="vertical" onFinish={v=>selected&&execute(selected.action,v.opinion)}><Form.Item label="处理意见" name="opinion" rules={[{required:!!selected&&['reject','reject-approval','sign-failed'].includes(selected.action),whitespace:true,message:'请填写处理意见'}]}><Input.TextArea rows={3}/></Form.Item></Form></Modal></>;
}
