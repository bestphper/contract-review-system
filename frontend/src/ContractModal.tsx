import { useState } from 'react';
import { Alert, Button, Form, Modal, Upload, message } from 'antd';
import type { UploadFile } from 'antd';
import { PaperClipOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { api } from './api';
import { ContractFields } from './ContractFields';

export function ContractModal({open,onCancel,onComplete}:{open:boolean;onCancel:()=>void;onComplete:()=>void}){
  const [form]=Form.useForm(); const qc=useQueryClient();
  const [files,setFiles]=useState<UploadFile[]>([]);
  const [contractId,setContractId]=useState<number>();
  const [busy,setBusy]=useState(false);
  const [uploadError,setUploadError]=useState('');
  const refresh=()=>{
    for(const key of ['contracts','dashboard','parties','audit-logs'])qc.invalidateQueries({queryKey:[key]});
    if(contractId)qc.invalidateQueries({queryKey:['attachments',contractId]});
  };
  const submit=async(values:unknown)=>{
    if(busy)return;
    setBusy(true);setUploadError('');
    let id=contractId;
    try{
      if(!id){id=await api.create(values);setContractId(id)}
      for(const file of files){
        if(!file.originFileObj)continue;
        await api.upload(id,file.originFileObj);
        setFiles(current=>current.filter(item=>item.uid!==file.uid));
        qc.invalidateQueries({queryKey:['attachments',id]});
      }
      refresh();message.success('合同及附件已保存');onComplete();
    }catch(e){
      const error=(e as Error).message;
      if(id){setUploadError(`合同已创建，附件上传失败：${error}`);refresh()}
      else message.error(error);
    }finally{setBusy(false)}
  };
  return <Modal className="contract-modal" open={open} width={900} title="新建合同"
    onCancel={()=>{if(!busy){refresh();onCancel()}}} maskClosable={!busy} closable={!busy}
    okText={contractId?'重试附件上传':'创建合同'} cancelText={contractId?'稍后上传':'取消'}
    onOk={()=>contractId?submit(form.getFieldsValue()):form.submit()} confirmLoading={busy}
    cancelButtonProps={{disabled:busy}} afterClose={()=>{form.resetFields();setFiles([]);setContractId(undefined);setUploadError('')}}>
    {uploadError&&<Alert className="contract-upload-error" type="warning" showIcon message={uploadError}/>}
    <Form form={form} layout="vertical" initialValues={{currency:'CNY'}} onFinish={submit} disabled={busy||!!contractId}>
      <ContractFields/>
    </Form>
    <div className="contract-attachment-section">
      <h3>合同附件</h3>
      <Upload multiple fileList={files} disabled={busy} beforeUpload={()=>false}
        onChange={({fileList})=>setFiles(fileList)}>
        <Button icon={<PaperClipOutlined/>} disabled={busy}>添加附件</Button>
      </Upload>
    </div>
  </Modal>;
}
